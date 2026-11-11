using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Editing line (.ttl) and trip (.ttp) files in place (ADR 092).</summary>
    public sealed class OmsiTimetableFilesTests
    {
        private const string Line =
            "-----------------------\r\nTime Table Line File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.pre.015\r\n\r\n[userallowed]\r\n\r\n[priority]\r\n1\r\n" +
            "------------------------------------\r\n\r\n[newtour]\r\n1\r\nBusses\r\n799\r\n\r\n------------------------------------\r\n\r\n  Dep.: 4:7:0\r\n[addtrip]\r\n76_BH-Kk\r\n0\r\n247.000\r\n\r\n" +
            "  Dep.: 4:22:0\r\n[addtrip]\r\n76_Kk-BH\r\n0\r\n262.000\r\n\r\n  Dep.: 4:37:0\r\n[addtrip]\r\n76_BH-Kk\r\n0\r\n277.000\r\n\r\n" +
            "------------------------------------\r\n\r\n[newtour]\r\n2\r\nBusses\r\n799\r\n\r\n------------------------------------\r\n\r\n  Dep.: 5:0:0\r\n[addtrip]\r\n76_Kk-BH\r\n1\r\n300.000\r\n\r\n";

        private static OmsiLineFile Open()
        {
            return OmsiLineFile.Parse(Encoding.UTF8.GetBytes(Line));
        }

        private static string Text(OmsiLineFile file)
        {
            return Encoding.UTF8.GetString(file.ToBytes());
        }

        [Test]
        public void AFileReadAndWrittenWithoutAChangeComesOutAsItWent()
        {
            Assert.AreEqual(Line, Text(Open()));
            var tours = Open().Tours;
            Assert.AreEqual(2, tours.Count);
            Assert.AreEqual("1", tours[0].Number);
            Assert.AreEqual("Busses", tours[0].VehicleGroup);
            Assert.AreEqual(3, tours[0].Trips.Count);
            Assert.AreEqual(262.0, tours[0].Trips[1].DepartureMinutes, 1e-9);
            Assert.AreEqual(1, tours[1].Trips[0].Profile);
        }

        [Test]
        public void ChangingADepartureChangesItsNumberAndItsCommentAndNothingElse()
        {
            var file = Open();
            file.SetDeparture(0, 1, 263.5);
            var text = Text(file);
            StringAssert.Contains("76_Kk-BH\r\n0\r\n263.500\r\n", text);
            StringAssert.Contains("  Dep.: 4:23:30\r\n[addtrip]\r\n76_Kk-BH", text);
            StringAssert.DoesNotContain("4:22:0", text);
            // The other comments and the header stay.
            StringAssert.Contains("  Dep.: 4:37:0\r\n", text);
            StringAssert.StartsWith("-----------------------\r\nTime Table Line File", text);
            Assert.AreEqual(263.5, file.Tours[0].Trips[1].DepartureMinutes, 1e-9);
        }

        [Test]
        public void ATripAddedGoesBehindTheLastOneThatLeavesNoLaterWithItsComment()
        {
            var file = Open();
            var position = file.AddTrip(0, "76_Kk-BH", 0, 270.0);
            Assert.AreEqual(2, position);
            var trips = file.Tours[0].Trips;
            CollectionAssert.AreEqual(new[] { 247.0, 262.0, 270.0, 277.0 }, trips.Select(trip => trip.DepartureMinutes).ToArray());
            var text = Text(file);
            StringAssert.Contains("247.000\r\n\r\n  Dep.: 4:22:0\r\n[addtrip]\r\n76_Kk-BH\r\n0\r\n262.000\r\n\r\n  Dep.: 4:30:0\r\n[addtrip]\r\n76_Kk-BH\r\n0\r\n270.000\r\n\r\n  Dep.: 4:37:0\r\n[addtrip]", text);
            // A trip before all others comes first, with its comment after the tour's head.
            file.AddTrip(0, "76_BH-Kk", 0, 200.0);
            Assert.AreEqual(200.0, file.Tours[0].Trips[0].DepartureMinutes, 1e-9);
            StringAssert.Contains("  Dep.: 3:20:0\r\n[addtrip]\r\n76_BH-Kk\r\n0\r\n200.000", Text(file));
            Assert.AreEqual(1, file.Tours[1].Trips.Count);
        }

        [Test]
        public void ASeriesLaysATripEverySoManyMinutesAndRemovingOneKeepsTheCommentsInOrder()
        {
            var file = Open();
            var count = file.AddSeries(1, "76_BH-Kk", 0, 320.0, 380.0, 20.0);
            Assert.AreEqual(4, count);
            CollectionAssert.AreEqual(new[] { 300.0, 320.0, 340.0, 360.0, 380.0 }, file.Tours[1].Trips.Select(trip => trip.DepartureMinutes).ToArray());
            file.RemoveTrip(1, 2);
            CollectionAssert.AreEqual(new[] { 300.0, 320.0, 360.0, 380.0 }, file.Tours[1].Trips.Select(trip => trip.DepartureMinutes).ToArray());
            var text = Text(file);
            StringAssert.DoesNotContain("5:40:0", text);
            StringAssert.Contains("  Dep.: 6:0:0\r\n[addtrip]\r\n76_BH-Kk\r\n0\r\n360.000", text);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => file.AddSeries(1, "x", 0, 0, 10, 0));
        }

        [Test]
        public void ShiftingATourMovesAllItsTripsAndStopsAtMidnight()
        {
            var file = Open();
            file.ShiftTour(0, 10.0);
            CollectionAssert.AreEqual(new[] { 257.0, 272.0, 287.0 }, file.Tours[0].Trips.Select(trip => trip.DepartureMinutes).ToArray());
            StringAssert.Contains("  Dep.: 4:17:0\r\n", Text(file));
            file.ShiftTour(0, -1000.0);
            Assert.IsTrue(file.Tours[0].Trips.All(trip => trip.DepartureMinutes == 0.0));
            Assert.AreEqual(300.0, file.Tours[1].Trips[0].DepartureMinutes, 1e-9);
        }

        [Test]
        public void ATourCanBeAddedRenamedAndRemovedWithItsTrips()
        {
            var file = Open();
            var index = file.AddTour("Sa 1", "Busses", 800);
            Assert.AreEqual(2, index);
            file.AddTrip(index, "76_BH-Kk", 0, 400.0);
            file.SetTour(index, "So 1", "Taxis");
            var tour = file.Tours[2];
            Assert.AreEqual("So 1", tour.Number);
            Assert.AreEqual("Taxis", tour.VehicleGroup);
            Assert.AreEqual(800, tour.DayMask);
            Assert.AreEqual(799, file.Tours[0].DayMask);
            file.SetTourDays(index, 960);
            Assert.AreEqual(960, file.Tours[2].DayMask);
            var empty = OmsiLineFile.Parse(Encoding.UTF8.GetBytes("[priority]\r\n1\r\n"));
            var added = empty.AddTour("1", "Busses");
            Assert.AreEqual(1023, empty.Tours[added].DayMask);
            Assert.AreEqual(1, tour.Trips.Count);
            var text = Text(file);
            // The new tour has the days it was given, and dashes stand before it as before the others.
            StringAssert.Contains("------------------------------------\r\n\r\n[newtour]\r\nSo 1\r\nTaxis\r\n960\r\n", text);
            StringAssert.Contains("  Dep.: 6:40:0\r\n[addtrip]\r\n76_BH-Kk\r\n0\r\n400.000", text);

            file.RemoveTour(0);
            Assert.AreEqual(2, file.Tours.Count);
            Assert.AreEqual("2", file.Tours[0].Number);
            StringAssert.DoesNotContain("4:7:0", Text(file));
            StringAssert.Contains("  Dep.: 5:0:0\r\n[addtrip]\r\n76_Kk-BH\r\n1\r\n300.000", Text(file));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => file.RemoveTour(5));
        }

        [Test]
        public void ATripFilesProfileRunningTimeChangesAndTheStopsStay()
        {
            var bytes = Encoding.UTF8.GetBytes("[trip]\r\n\r\nKrankenhaus\r\n76\r\n\r\n[station_typ2]\r\n108\r\n\r\n[profile]\r\nstandard\r\n7.000\r\n\r\n[profile]\r\nNachtbus\r\n12.000\r\n\r\n[profile_man_dep_time]\r\n3\r\n2.000\r\n");
            var file = OmsiTripFile.Parse(bytes);
            Assert.AreEqual("Krankenhaus", file.Destination);
            Assert.AreEqual("76", file.Line);
            Assert.AreEqual(2, file.Profiles.Count);
            file.SetRunningMinutes(1, 14.25);
            var text = Encoding.UTF8.GetString(file.ToBytes());
            StringAssert.Contains("[profile]\r\nNachtbus\r\n14.250\r\n", text);
            StringAssert.Contains("[profile]\r\nstandard\r\n7.000\r\n", text);
            StringAssert.Contains("[station_typ2]\r\n108\r\n", text);
            StringAssert.Contains("[profile_man_dep_time]\r\n3\r\n2.000\r\n", text);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => file.SetRunningMinutes(5, 1.0));
        }
    }
}
