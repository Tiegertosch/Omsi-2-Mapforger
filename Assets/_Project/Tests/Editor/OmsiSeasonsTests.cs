using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiSeasonsTests
    {
        private const string Config =
            "[name]\r\nTestmap\r\n\r\n[groundtex]\r\nTexture\\gras.bmp\r\nTexture\\gras_det.bmp\r\n0\r\n1\r\n60\r\n\r\n" +
            " Winter (snow):\r\n[addseason]\r\n4\r\n0\r\n38\r\n\r\n Winter:\r\n[addseason]\r\n3\r\n38\r\n75\r\n\r\n" +
            " Spring:\r\n[addseason]\r\n1\r\n76\r\n134\r\n\r\n[trafficdensity_road]\r\n0\r\n0.5\r\n";

        private static OmsiRecordFile Parse(string text)
        {
            return OmsiRecordFile.Parse(OmsiTextFile.Encode(text, OmsiTextEncoding.Utf16LittleEndian), OmsiGlobalConfigLayouts.Instance);
        }

        private static string Text(OmsiRecordFile file)
        {
            return OmsiTextFile.Decode(file.ToBytes());
        }

        [Test]
        public void ReadsTheEntriesInOrder()
        {
            var entries = OmsiSeasons.Read(Parse(Config));
            CollectionAssert.AreEqual(new[] { new OmsiSeasonEntry(4, 0, 38), new OmsiSeasonEntry(3, 38, 75), new OmsiSeasonEntry(1, 76, 134) }, entries);
        }

        [Test]
        public void WritingTheEntriesAsTheyAreChangesNothingByteForByte()
        {
            var bytes = OmsiTextFile.Encode(Config, OmsiTextEncoding.Utf16LittleEndian);
            var file = OmsiRecordFile.Parse(bytes, OmsiGlobalConfigLayouts.Instance);
            Assert.IsFalse(OmsiSeasons.Apply(file, OmsiSeasons.Read(file)));
            CollectionAssert.AreEqual(bytes, file.ToBytes());
        }

        [Test]
        public void ChangingOneEntryRewritesOnlyItsFieldsAndItsComment()
        {
            var file = Parse(Config);
            var entries = OmsiSeasons.Read(file);
            entries[1] = new OmsiSeasonEntry(OmsiSeasonKind.Autumn, 40, 80);
            Assert.IsTrue(OmsiSeasons.Apply(file, entries));
            var expected = Config.Replace(" Winter:\r\n[addseason]\r\n3\r\n38\r\n75", " Autumn:\r\n[addseason]\r\n2\r\n40\r\n80");
            Assert.AreEqual(expected, Text(file));
        }

        [Test]
        public void ChangingADayKeepsTheComment()
        {
            var file = Parse(Config);
            var entries = OmsiSeasons.Read(file);
            entries[2] = new OmsiSeasonEntry(OmsiSeasonKind.Spring, 76, 140);
            Assert.IsTrue(OmsiSeasons.Apply(file, entries));
            Assert.AreEqual(Config.Replace("76\r\n134", "76\r\n140"), Text(file));
        }

        [Test]
        public void AddingAnEntryPutsItAfterTheLastWithAComment()
        {
            var file = Parse(Config);
            var entries = OmsiSeasons.Read(file);
            entries.Add(new OmsiSeasonEntry(OmsiSeasonKind.Autumn, 260, 323));
            Assert.IsTrue(OmsiSeasons.Apply(file, entries));
            var expected = Config.Replace(" Spring:\r\n[addseason]\r\n1\r\n76\r\n134\r\n\r\n",
                " Spring:\r\n[addseason]\r\n1\r\n76\r\n134\r\n\r\n Autumn:\r\n[addseason]\r\n2\r\n260\r\n323\r\n\r\n");
            Assert.AreEqual(expected, Text(file));
            CollectionAssert.AreEqual(entries, OmsiSeasons.Read(Parse(Text(file))));
        }

        [Test]
        public void AddingToAMapWithoutSeasonsPutsThemAfterTheGroundAndRemovingAllLeavesNone()
        {
            var file = Parse("[name]\r\nTestmap\r\n\r\n[groundtex]\r\na.bmp\r\nb.bmp\r\n0\r\n1\r\n60\r\n\r\n[map]\r\n0\r\n0\r\nt.map\r\n");
            Assert.IsTrue(OmsiSeasons.Apply(file, new[] { new OmsiSeasonEntry(3, 0, 80) }));
            var names = file.Records.Select(record => record.Name).ToList();
            CollectionAssert.AreEqual(new[] { "name", "groundtex", "addseason", "map" }, names);
            Assert.IsTrue(OmsiSeasons.Apply(file, new OmsiSeasonEntry[0]));
            CollectionAssert.AreEqual(new[] { "name", "groundtex", "map" }, file.Records.Select(record => record.Name).ToList());
            StringAssert.DoesNotContain("Winter:", Text(file));
        }

        [Test]
        public void RemovingAnEntryTakesItsCommentAndKeepsTheRest()
        {
            var file = Parse(Config);
            var entries = OmsiSeasons.Read(file);
            entries.RemoveAt(2);
            Assert.IsTrue(OmsiSeasons.Apply(file, entries));
            var text = Text(file);
            StringAssert.DoesNotContain("Spring:", text);
            StringAssert.DoesNotContain("134", text);
            StringAssert.Contains(" Winter:\r\n[addseason]\r\n3\r\n38\r\n75\r\n\r\n[trafficdensity_road]\r\n0\r\n0.5\r\n", text);
            StringAssert.Contains("[groundtex]\r\nTexture\\gras.bmp", text);
        }

        [Test]
        public void RemovingTheLastEntryKeepsAForeignCommentAboveTheNextSection()
        {
            var text = Config.Replace("134\r\n\r\n[trafficdensity_road]", "134\r\n\r\n Traffic on roads\r\n\r\n[trafficdensity_road]");
            var file = Parse(text);
            var entries = OmsiSeasons.Read(file);
            entries.RemoveAt(2);
            Assert.IsTrue(OmsiSeasons.Apply(file, entries));
            var result = Text(file);
            StringAssert.DoesNotContain("Spring:", result);
            StringAssert.Contains("3\r\n38\r\n75\r\n\r\n Traffic on roads\r\n\r\n[trafficdensity_road]\r\n0\r\n0.5\r\n", result);
        }

        [Test]
        public void TwoEqualSingleDaySeasonsOverlapButAdjacentRangesSharingADayDoNot()
        {
            Assert.IsTrue(OmsiSeasons.Overlaps(new OmsiSeasonEntry(3, 10, 10), new OmsiSeasonEntry(1, 10, 10)));
            Assert.IsFalse(OmsiSeasons.Overlaps(new OmsiSeasonEntry(3, 0, 10), new OmsiSeasonEntry(1, 10, 20)));
            Assert.AreEqual(OmsiSeasonProblem.Overlap, OmsiSeasons.Check(new[] { new OmsiSeasonEntry(3, 10, 10), new OmsiSeasonEntry(1, 10, 10) }).Single().Problem);
        }

        [Test]
        public void RemovingAnEntryFromTheMiddleLeavesTheOthersWithTheirOwnCommentsUntouched()
        {
            var text = Config.Replace(" Spring:", " Fasching:\r\n Spring:");
            var file = Parse(text);
            var entries = OmsiSeasons.Read(file);
            entries.RemoveAt(1);
            Assert.IsTrue(OmsiSeasons.Apply(file, entries));
            var expected = text.Replace(" Winter:\r\n[addseason]\r\n3\r\n38\r\n75\r\n\r\n", string.Empty);
            Assert.AreEqual(expected, Text(file));
            CollectionAssert.AreEqual(entries, OmsiSeasons.Read(Parse(Text(file))));
        }

        [Test]
        public void AddingWithoutGroundTexturesPutsTheSeasonBeforeTheTrafficDensities()
        {
            var file = Parse("[name]\r\nTestmap\r\n\r\n[trafficdensity_road]\r\n0\r\n0.5\r\n");
            Assert.IsTrue(OmsiSeasons.Apply(file, new[] { new OmsiSeasonEntry(3, 0, 80) }));
            CollectionAssert.AreEqual(new[] { "name", "addseason", "trafficdensity_road" }, file.Records.Select(record => record.Name).ToList());
            Assert.IsTrue(OmsiSeasons.Apply(file, new[] { new OmsiSeasonEntry(3, 0, 80), new OmsiSeasonEntry(1, 81, 100) }));
            CollectionAssert.AreEqual(new[] { "name", "addseason", "addseason", "trafficdensity_road" }, file.Records.Select(record => record.Name).ToList());
        }

        [Test]
        public void ASeasonMayStartWhereTheLastEndsButRealOverlapIsReported()
        {
            Assert.IsFalse(OmsiSeasons.Check(OmsiSeasons.Read(Parse(Config))).Any(), "38 to 38 is how the installed maps join seasons");
            var findings = OmsiSeasons.Check(new[] { new OmsiSeasonEntry(3, 0, 80), new OmsiSeasonEntry(1, 70, 134) });
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(OmsiSeasonProblem.Overlap, findings[0].Problem);
            Assert.AreEqual(1, findings[0].Index);
            Assert.AreEqual(0, findings[0].Other);
            Assert.IsFalse(findings[0].IsError, "An overlap is reported but does not stop the settings.");
        }

        [Test]
        public void DaysOutsideTheYearAndReversedRangesAreErrors()
        {
            Assert.AreEqual(OmsiSeasonProblem.DayOutOfRange, OmsiSeasons.Check(new[] { new OmsiSeasonEntry(3, -1, 10) })[0].Problem);
            Assert.AreEqual(OmsiSeasonProblem.DayOutOfRange, OmsiSeasons.Check(new[] { new OmsiSeasonEntry(3, 0, 371) })[0].Problem);
            Assert.AreEqual(OmsiSeasonProblem.LastBeforeFirst, OmsiSeasons.Check(new[] { new OmsiSeasonEntry(3, 50, 10) })[0].Problem);
            Assert.AreEqual(OmsiSeasonProblem.NegativeKind, OmsiSeasons.Check(new[] { new OmsiSeasonEntry(-1, 0, 10) })[0].Problem);
            Assert.IsNull(OmsiSeasons.FirstError(new[] { new OmsiSeasonEntry(3, 0, 370) }));
            Assert.IsNotNull(OmsiSeasons.FirstError(new[] { new OmsiSeasonEntry(3, 50, 10) }));
            var file = Parse(Config);
            Assert.Throws<ArgumentException>(() => OmsiSeasons.Apply(file, new[] { new OmsiSeasonEntry(3, 50, 10) }));
            Assert.AreEqual(Config, Text(file), "A refused list changes nothing.");
        }

        [Test]
        public void AnUnknownKindIsKeptAndOnlyReportedAsAWarning()
        {
            var findings = OmsiSeasons.Check(new[] { new OmsiSeasonEntry(9, 0, 10) });
            Assert.AreEqual(OmsiSeasonProblem.UnknownKind, findings.Single().Problem);
            Assert.IsFalse(findings.Single().IsError);
        }

        [Test]
        public void TheSettingsOptionsCarryTheEntriesOfTheFileAndWriteChangedOnes()
        {
            var file = Parse(Config);
            var options = OmsiNewMapOptions.FromTemplate(file);
            Assert.AreEqual(3, options.SeasonEntries.Count);
            options.SeasonEntries[0] = new OmsiSeasonEntry(OmsiSeasonKind.Winter, 0, 38);
            Assert.IsNull(options.Check());
            options.SeasonEntries.Add(new OmsiSeasonEntry(OmsiSeasonKind.Summer, 200, 100));
            Assert.IsNotNull(options.Check());
        }
    }
}
