using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The stops of a trip and what its profiles say of single stops (ADR 096).</summary>
    public sealed class OmsiTripStopsTests
    {
        private const string Comment = "\r\n.........................\r\n        Profiles\r\n.........................\r\n\r\n";

        private const string Trip =
            "-----------------------\r\nTime Table Trip File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.pre.015\r\n\r\n[trip]\r\n\r\nKrankenhaus\r\n76\r\n\r\n" +
            ".........................\r\n        Stations\r\n.........................\r\n\r\n" +
            "[station_typ2]\r\n108\r\n\r\n[station_typ2]\r\n104\r\n\r\n[station_typ2]\r\n129\r\n\r\n[station_typ2]\r\n763\r\n" + Comment +
            "[profile]\r\nstandard\r\n7.000\r\n\r\n[profile_man_dep_time]\r\n1\r\n2.000\r\n\r\n[profile_man_dep_time]\r\n3\r\n6.000\r\n\r\n" +
            "[profile_otherstopping]\r\n2\r\n2\r\n\r\n[profile]\r\nsecond\r\n9.000\r\n\r\n";

        private static OmsiTripFile Open()
        {
            return OmsiTripFile.Parse(Encoding.UTF8.GetBytes(Trip));
        }

        private static string Text(OmsiTripFile file)
        {
            return Encoding.UTF8.GetString(file.ToBytes());
        }

        [Test]
        public void ReadsTheStopsAndTheirSettingsAndWritesThemBackUnchanged()
        {
            var file = Open();
            Assert.AreEqual(Trip, Text(file));
            CollectionAssert.AreEqual(new[] { 108, 104, 129, 763 }, file.StopIds);
            Assert.IsFalse(file.HasTrack);
            var settings = file.GetStopSettings(0);
            CollectionAssert.AreEqual(new[] { 1, 3 }, settings.Departures.Keys.OrderBy(key => key).ToArray());
            Assert.AreEqual(6.0, settings.Departures[3], 1e-9);
            Assert.AreEqual(2, settings.Stopping[2]);
            Assert.AreEqual(0, file.GetStopSettings(1).Departures.Count);
        }

        [Test]
        public void StopsCanBeInsertedAndTheSettingsFollowTheirStops()
        {
            var file = Open();
            file.SetStops(new[] { 108, 500, 104, 129, 763 });
            CollectionAssert.AreEqual(new[] { 108, 500, 104, 129, 763 }, file.StopIds);
            var settings = file.GetStopSettings(0);
            // The stop 104 was the second and is now the third; 763 the fourth and now the fifth; 129 the third and now the fourth.
            CollectionAssert.AreEqual(new[] { 2, 4 }, settings.Departures.Keys.OrderBy(key => key).ToArray());
            Assert.AreEqual(2, settings.Stopping[3]);
            // The comment above the profiles still follows the last stop, and no other line moved.
            var text = Text(file);
            StringAssert.Contains("[station_typ2]\r\n763\r\n" + Comment + "[profile]", text);
            StringAssert.Contains("[station_typ2]\r\n500\r\n\r\n[station_typ2]\r\n104", text);
            Assert.AreEqual(Trip.Split('\n').Length + 3, text.Split('\n').Length);
        }

        [Test]
        public void StopsCanBeTakenOutAndTheirSettingsGoWithThem()
        {
            var file = Open();
            file.SetStops(new[] { 108, 129, 763 });
            CollectionAssert.AreEqual(new[] { 108, 129, 763 }, file.StopIds);
            var settings = file.GetStopSettings(0);
            // 104 (the second) is gone with its time; 763 moves up; 129 keeps its stopping setting under its new number.
            CollectionAssert.AreEqual(new[] { 2 }, settings.Departures.Keys.ToArray());
            Assert.AreEqual(6.0, settings.Departures[2], 1e-9);
            Assert.AreEqual(2, settings.Stopping[1]);
            StringAssert.Contains("[station_typ2]\r\n763\r\n" + Comment + "[profile]", Text(file));
            file.SetStops(new[] { 108, 129, 763 });
            var again = OmsiTripFile.Parse(file.ToBytes());
            CollectionAssert.AreEqual(new[] { 108, 129, 763 }, again.StopIds);
        }

        [Test]
        public void ARingLineKeepsBothVisitsOfAStopInOrder()
        {
            var file = Open();
            file.SetStops(new[] { 108, 104, 129, 108 });
            CollectionAssert.AreEqual(new[] { 108, 104, 129, 108 }, file.StopIds);
            file.SetStops(new[] { 108, 104, 129, 108, 104 });
            CollectionAssert.AreEqual(new[] { 108, 104, 129, 108, 104 }, file.StopIds);
            Assert.Throws<ArgumentException>(() => file.SetStops(new[] { 108 }));
        }

        [Test]
        public void TimesAndStoppingOfAStopAreSetAndTakenAwayInTheOrderTheEditorWritesThem()
        {
            var file = Open();
            file.SetArrival(0, 3, 5.5);
            file.SetDeparture(0, 2, 4.0);
            file.SetStopping(0, 0, 3);
            file.SetDeparture(0, 3, 6.5);
            var text = Text(file);
            Assert.Less(text.IndexOf("[profile_man_arr_time]\r\n3\r\n5.500", StringComparison.Ordinal), text.IndexOf("[profile_man_dep_time]\r\n1", StringComparison.Ordinal));
            Assert.Less(text.IndexOf("[profile_man_dep_time]\r\n2\r\n4.000", StringComparison.Ordinal), text.IndexOf("[profile_otherstopping]\r\n2", StringComparison.Ordinal));
            Assert.Less(text.IndexOf("[profile_otherstopping]\r\n0\r\n3", StringComparison.Ordinal), text.IndexOf("[profile]\r\nsecond", StringComparison.Ordinal));
            var settings = file.GetStopSettings(0);
            Assert.AreEqual(6.5, settings.Departures[3], 1e-9);
            Assert.AreEqual(5.5, settings.Arrivals[3], 1e-9);
            Assert.AreEqual(3, settings.Stopping[0]);
            Assert.AreEqual(0, file.GetStopSettings(1).Arrivals.Count);

            file.SetArrival(0, 3, null);
            file.SetDeparture(0, 2, null);
            file.SetStopping(0, 0, null);
            file.SetDeparture(0, 3, 6.0);
            Assert.AreEqual(Trip, Text(file));
            Assert.Throws<ArgumentOutOfRangeException>(() => file.SetStopping(0, 0, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => file.SetArrival(0, 9, 1.0));
        }

        [Test]
        public void ATripWithATrackIsNotEditedHere()
        {
            var track = "[trip]\r\nRB\r\nHalberstadt\r\n\r\n[station]\r\n8789\r\n4\r\nGrundorf\r\n3\r\n-0.2\r\n789.277\r\n89.277\r\n5.000\r\n\r\n[profile]\r\nstandard\r\n4.000\r\n\r\n";
            var file = OmsiTripFile.Parse(Encoding.UTF8.GetBytes(track));
            Assert.IsTrue(file.HasTrack);
            Assert.Throws<InvalidOperationException>(() => file.SetStops(new[] { 1, 2 }));
        }

        [Test]
        public void TheDestinationCanBeChanged()
        {
            var file = Open();
            file.SetDestination("Bauernhof");
            StringAssert.Contains("[trip]\r\n\r\nBauernhof\r\n76", Text(file));
            Assert.AreEqual("Bauernhof", file.Destination);
        }
    }
}
