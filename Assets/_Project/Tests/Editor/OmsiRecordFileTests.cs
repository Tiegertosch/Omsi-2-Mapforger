using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiRecordFileTests
    {
        private const string Tile =
            "File created with OMSI-Editor on 26.11.2013 22:04:30\r\n" +
            "\r\n" +
            "[version]\r\n" +
            "14\r\n" +
            "\r\n" +
            "[object]\r\n" +
            "0\r\n" +
            "Sceneryobjects\\Trees\\tree.sco\r\n" +
            "17\r\n" +
            "25.5\r\n" +
            "243.25\r\n" +
            "1.5\r\n" +
            "90\r\n" +
            "0\r\n" +
            "0\r\n" +
            "2\r\n" +
            "13N\r\n" +
            "\r\n" +
            "\r\n" +
            "Object Nr. 1\r\n" +
            "[rule]\r\n" +
            "0\r\n" +
            "trafficdensity\r\n" +
            "1.000\r\n" +
            "0\r\n" +
            "\r\n" +
            "[spline]\r\n" +
            "0\r\n" +
            "Splines\\road.sli\r\n" +
            "42\r\n" +
            "41\r\n" +
            "0\r\n" +
            "18.25\r\n" +
            "2.5\r\n" +
            "147\r\n" +
            "445.6\r\n" +
            "143.18\r\n" +
            "-120\r\n" +
            "0\r\n0\r\n0\r\n0\r\n0\r\n0\r\n0\r\n" +
            "mirror\r\n" +
            "\r\n" +
            "[unknown_section]\r\n" +
            "kept as free text\r\n";

        [Test]
        public void RoundTripsAUtf16TileByteForByte()
        {
            var bytes = Utf16(Tile);

            var file = OmsiRecordFile.Parse(bytes, OmsiTileRecordLayouts.Instance);

            Assert.IsTrue(file.IsExactRoundTrip);
            CollectionAssert.AreEqual(bytes, file.ToBytes());
            Assert.AreEqual(OmsiTextEncoding.Utf16LittleEndian, file.Encoding);
            Assert.AreEqual(14, file.Version);
            Assert.AreEqual(2, file.Preamble.Count);
            CollectionAssert.AreEqual(new[] { "version", "object", "rule", "spline", "unknown_section" }, file.Records.Select(record => record.Name).ToArray());
        }

        [Test]
        public void SplitsRecordsIntoLayoutFieldsAndTrailingFreeText()
        {
            var file = OmsiRecordFile.Parse(Utf16(Tile), OmsiTileRecordLayouts.Instance);
            var item = file.Records[1];
            var spline = file.Records[3];
            var unknown = file.Records[4];

            // 9 fixed fields, the label count and two labels, one of them empty.
            Assert.AreEqual(12, item.Fields.Count);
            Assert.AreEqual("13N", item.GetField(10));
            Assert.AreEqual(string.Empty, item.GetField(11));
            Assert.AreEqual(2, item.Trailer.Count);
            Assert.AreEqual("Object Nr. 1", item.Trailer[1].Text);
            Assert.AreEqual(4, file.Records[2].Fields.Count);
            Assert.AreEqual(19, spline.Fields.Count);
            Assert.AreEqual("mirror", spline.GetField(18));
            Assert.IsFalse(unknown.HasKnownLayout);
            Assert.AreEqual(0, unknown.Fields.Count);
            Assert.AreEqual("kept as free text", unknown.Trailer[0].Text);
        }

        [Test]
        public void KeepsALabelThatLooksLikeASectionHeaderInsideItsRecord()
        {
            var text = "[version]\r\n14\r\n[object]\r\n0\r\na.sco\r\n5\r\n1\r\n2\r\n0\r\n0\r\n0\r\n0\r\n1\r\n[spline]\r\n\r\n";

            var file = OmsiRecordFile.Parse(Utf16(text), OmsiTileRecordLayouts.Instance);

            Assert.AreEqual(2, file.Records.Count);
            Assert.AreEqual("[spline]", file.Records[1].GetField(10));
            Assert.IsTrue(file.IsExactRoundTrip);
        }

        [Test]
        public void ReadsSplineHWithItsExtraHeightField()
        {
            var fields = string.Join("\r\n", "0", "rail.sli", "7", "0", "0", "1", "30", "2", "90", "10", "0", "0", "0", "-0.5", "0", "0", "0", "0", "10", "mirror");
            var file = OmsiRecordFile.Parse(Utf16("[version]\r\n14\r\n[spline_h]\r\n" + fields + "\r\n"), OmsiTileRecordLayouts.Instance);

            Assert.AreEqual(20, file.Records[1].Fields.Count);
            Assert.IsTrue(file.Records[1].TryGetDouble(13, out var heightDifference));
            Assert.AreEqual(-0.5, heightDifference, 1e-12);
        }

        [Test]
        public void PreservesMixedLineEndingsAndAMissingFinalLineBreak()
        {
            var bytes = Encoding.UTF8.GetBytes("[busstop]\nNordspitze\r\n3\r129\r\n0.0000000000");

            var file = OmsiRecordFile.Parse(bytes, null);

            Assert.AreEqual(OmsiTextEncoding.Utf8, file.Encoding);
            Assert.IsTrue(file.IsExactRoundTrip);
            CollectionAssert.AreEqual(bytes, file.ToBytes());
            Assert.AreEqual(string.Empty, file.Records[0].Trailer.Last().Ending);
        }

        [Test]
        public void RoundTripsWindows1252IncludingUndefinedBytes()
        {
            // "Straße", then the undefined Windows-1252 bytes 0x81 and 0x9D, then the euro sign 0x80.
            var bytes = new byte[] { 0x5B, 0x61, 0x5D, 0x0D, 0x0A, 0x53, 0x74, 0x72, 0x61, 0xDF, 0x65, 0x81, 0x9D, 0x80, 0x0D, 0x0A };

            var file = OmsiRecordFile.Parse(bytes, null);

            Assert.AreEqual(OmsiTextEncoding.Windows1252, file.Encoding);
            Assert.AreEqual("Straße\u0081\u009D€", file.Records[0].Trailer[0].Text);
            CollectionAssert.AreEqual(bytes, file.ToBytes());
        }

        [Test]
        public void EditingAFieldChangesOnlyThatLine()
        {
            var file = OmsiRecordFile.Parse(Utf16(Tile), OmsiTileRecordLayouts.Instance);

            file.Records[1].SetField(3, "30.75");

            Assert.AreEqual(Tile.Replace("\r\n25.5\r\n", "\r\n30.75\r\n"), Encoding.Unicode.GetString(file.ToBytes(), 2, file.ToBytes().Length - 2));
            Assert.Throws<System.ArgumentException>(() => file.Records[1].SetField(3, "1\r\n2"));
        }

        [Test]
        public void KeepsIncompleteAndUnsizableRecordsLossless()
        {
            var invalidCount = "[version]\r\n14\r\n[object]\r\n0\r\na.sco\r\n5\r\n1\r\n2\r\n0\r\n0\r\n0\r\n0\r\nmany\r\nlabel\r\n";
            var truncated = "[version]\r\n14\r\n[spline]\r\n0\r\nroad.sli\r\n1\r\n";

            var unsizable = OmsiRecordFile.Parse(Utf16(invalidCount), OmsiTileRecordLayouts.Instance);
            var cutOff = OmsiRecordFile.Parse(Utf16(truncated), OmsiTileRecordLayouts.Instance);

            Assert.IsFalse(unsizable.Records[1].IsComplete);
            Assert.AreEqual(0, unsizable.Records[1].Fields.Count);
            Assert.IsTrue(unsizable.IsExactRoundTrip);
            Assert.IsFalse(cutOff.Records[1].IsComplete);
            Assert.AreEqual(3, cutOff.Records[1].Fields.Count);
            Assert.IsTrue(cutOff.IsExactRoundTrip);
        }

        [Test]
        public void ReportsTextThatCannotBeWrittenBackExactly()
        {
            // An unpaired UTF-16 surrogate is replaced while decoding.
            var bytes = new byte[] { 0xFF, 0xFE, 0x5B, 0x00, 0x61, 0x00, 0x5D, 0x00, 0x00, 0xD8 };

            var file = OmsiRecordFile.Parse(bytes, null);

            Assert.IsFalse(file.IsExactRoundTrip);
        }

        [Test]
        public void LeavesUnverifiedOlderLayoutsAsFreeText()
        {
            var file = OmsiRecordFile.Parse(Utf16("[version]\r\n10\r\n[spline]\r\n0\r\nroad.sli\r\n-1\r\n"), OmsiTileRecordLayouts.Instance);

            Assert.IsFalse(file.Records[1].HasKnownLayout);
            Assert.AreEqual(3, file.Records[1].Trailer.Count);
        }

        [Test]
        public void RoundTripCheckCoversTextFilesAndSkipsBinaryOnes()
        {
            var folder = Path.Combine(Path.GetTempPath(), "mapforge-roundtrip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "TTData"));
            try
            {
                File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map"), Utf16(Tile));
                File.WriteAllBytes(Path.Combine(folder, "global.cfg"), Utf16("[name]\r\nTest\r\n"));
                File.WriteAllBytes(Path.Combine(folder, "TTData", "Busstops.cfg"), Encoding.UTF8.GetBytes("[busstop]\nA\n"));
                File.WriteAllBytes(Path.Combine(folder, "tile_0_0.map.terrain"), new byte[] { 60, 0, 0, 0 });

                var report = OmsiRoundTripCheck.Run(folder);

                Assert.IsTrue(report.Passed);
                Assert.AreEqual(3, report.FileCount);
                Assert.AreEqual(3, report.ExactCount);
                Assert.AreEqual(5, report.TileRecordCount);
                Assert.AreEqual(1, report.TileRecordsWithoutLayout);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static byte[] Utf16(string text)
        {
            return OmsiTextFile.Encode(text, OmsiTextEncoding.Utf16LittleEndian);
        }
    }
}
