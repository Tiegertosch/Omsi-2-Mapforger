using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTrafficCurveTests
    {
        private const string Config =
            "[NextIDCode]\r\n100\r\n\r\n[trafficdensity_road]\r\n0.000\r\n0.100\r\n\r\n[trafficdensity_road]\r\n6.000\r\n1.000\r\n\r\n[trafficdensity_road]\r\n12.000\r\n0.500\r\n\r\n" +
            "[trafficdensity_passenger]\r\n0.000\r\n0.200\r\n\r\n[addseason]\r\n0\r\n0\r\n38\r\n";

        private static OmsiRecordFile Parse(string text)
        {
            return OmsiRecordFile.Parse(Encoding.ASCII.GetBytes(text), OmsiGlobalConfigLayouts.Instance);
        }

        [Test]
        public void ACurveIsReadSortedAndJoinedByStraightLinesAsOpenOmsiDoes()
        {
            var points = OmsiTrafficCurve.Read(Parse(Config), OmsiTrafficCurve.CarSection);
            Assert.AreEqual(3, points.Count);
            // Constant before the first and after the last point, straight in between.
            Assert.AreEqual(0.1, OmsiTrafficCurve.ValueAt(points, -1), 1e-9);
            Assert.AreEqual(0.55, OmsiTrafficCurve.ValueAt(points, 3), 1e-9);
            Assert.AreEqual(0.75, OmsiTrafficCurve.ValueAt(points, 9), 1e-9);
            Assert.AreEqual(0.5, OmsiTrafficCurve.ValueAt(points, 20), 1e-9);
            // A map with no points has a factor of 1 all day.
            Assert.AreEqual(1.0, OmsiTrafficCurve.ValueAt(new List<KeyValuePair<double, double>>(), 8), 1e-9);
            Assert.AreEqual(OmsiTrafficCurve.HourCount, OmsiTrafficCurve.Hourly(points).Length);
        }

        [Test]
        public void SimplifyKeepsOnlyTheHoursWhereTheCurveBends()
        {
            var hourly = OmsiTrafficCurve.Hourly(OmsiTrafficCurve.Read(Parse(Config), OmsiTrafficCurve.CarSection));
            var points = OmsiTrafficCurve.Simplify(hourly);
            // 0, 6 and 12 bend; 24 ends the curve: the straight hours between are left out.
            CollectionAssert.AreEqual(new[] { 0.0, 6.0, 12.0, 24.0 }, points.Select(point => point.Key));
            Assert.AreEqual(0.5, points[3].Value, 1e-9);
            // A flat curve is two points, and values are kept between 0 and 3.
            var flat = OmsiTrafficCurve.Simplify(Enumerable.Repeat(5.0, OmsiTrafficCurve.HourCount).ToArray());
            Assert.AreEqual(2, flat.Count);
            Assert.AreEqual(OmsiTrafficCurve.MaximumFactor, flat[0].Value, 1e-9);
        }

        [Test]
        public void WritingACurveReplacesItsPointsInPlaceAndLeavesTheRestOfTheFile()
        {
            var config = Parse(Config);
            var hourly = Enumerable.Repeat(1.0, OmsiTrafficCurve.HourCount).ToArray();
            hourly[8] = 1.5;
            OmsiTrafficCurve.Write(config, OmsiTrafficCurve.CarSection, OmsiTrafficCurve.Simplify(hourly));
            var names = config.Records.Select(record => record.Name).ToList();
            // The new points stand where the first old one stood: after NextIDCode, before the passenger curve.
            Assert.AreEqual("NextIDCode", names[0]);
            Assert.AreEqual(names.Count(name => name == OmsiTrafficCurve.CarSection), names.LastIndexOf(OmsiTrafficCurve.CarSection) - names.IndexOf(OmsiTrafficCurve.CarSection) + 1);
            Assert.Less(names.LastIndexOf(OmsiTrafficCurve.CarSection), names.IndexOf(OmsiTrafficCurve.PassengerSection));
            var written = OmsiTrafficCurve.Read(Parse(Encoding.ASCII.GetString(config.ToBytes())), OmsiTrafficCurve.CarSection);
            Assert.AreEqual(1.5, OmsiTrafficCurve.ValueAt(written, 8), 1e-9);
            Assert.AreEqual(1.0, OmsiTrafficCurve.ValueAt(written, 5), 1e-9);
            Assert.AreEqual(1, OmsiTrafficCurve.Read(config, OmsiTrafficCurve.PassengerSection).Count);
            Assert.AreEqual(1, names.Count(name => name == "addseason"));
        }

        [Test]
        public void ACurveANewMapLacksIsWrittenAfterTheOtherDensityCurvesOrTheSeasons()
        {
            var config = Parse("[NextIDCode]\r\n100\r\n\r\n[addseason]\r\n0\r\n0\r\n38\r\n");
            OmsiTrafficCurve.Write(config, OmsiTrafficCurve.PassengerSection, OmsiTrafficCurve.Simplify(Enumerable.Repeat(0.5, OmsiTrafficCurve.HourCount).ToArray()));
            var names = config.Records.Select(record => record.Name).ToList();
            CollectionAssert.AreEqual(new[] { "NextIDCode", "addseason", OmsiTrafficCurve.PassengerSection, OmsiTrafficCurve.PassengerSection }, names);
        }
    }
}
