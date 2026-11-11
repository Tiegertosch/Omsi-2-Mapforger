using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One trip of a tour in a line file (<c>[addtrip]</c>): the trip's name, the index of its profile and its departure in minutes after midnight.</summary>
    public sealed class OmsiLineTrip
    {
        public string Trip { get; internal set; }
        public int Profile { get; internal set; }
        public double DepartureMinutes { get; internal set; }
    }

    /// <summary>One tour of a line file (<c>[newtour]</c>): its name (which may carry the days it runs, "Mo-Fr 1"), its vehicle group and its trips.</summary>
    public sealed class OmsiLineTour
    {
        public string Number { get; internal set; }
        public string VehicleGroup { get; internal set; }

        /// <summary>
        /// The days the tour runs, the third number of <c>[newtour]</c>: bits 0 to 6 are Monday to Sunday, bit 7 is public holidays, bits 8 and 9 are
        /// the two kinds of school days (Mo-Fr is 799, Sa 800, So with holidays 960, all days 1023); -1 when the file has no readable number.
        /// </summary>
        public int DayMask { get; internal set; } = -1;

        public List<OmsiLineTrip> Trips { get; } = new List<OmsiLineTrip>();
    }

    /// <summary>
    /// A line file (<c>TTData\x.ttl</c>) opened for editing (ADR 092): departures, trips and tours change in place and everything else, such as
    /// the header, the dashes between the tours and records this class does not know, stays as it is. The comment <c>Dep.: h:m:s</c> OMSI's
    /// editor writes before each trip is kept in step with its departure.
    /// </summary>
    public sealed class OmsiLineFile
    {
        private const string Dashes = "------------------------------------";

        private readonly OmsiRecordFile file;

        private OmsiLineFile(OmsiRecordFile file)
        {
            this.file = file;
        }

        public static OmsiLineFile Parse(byte[] bytes)
        {
            return new OmsiLineFile(OmsiRecordFile.Parse(bytes, null));
        }

        public byte[] ToBytes()
        {
            return file.ToBytes();
        }

        /// <summary>The tours and their trips as the file has them now.</summary>
        public List<OmsiLineTour> Tours
        {
            get
            {
                var result = new List<OmsiLineTour>();
                OmsiLineTour tour = null;
                foreach (var record in file.Records)
                {
                    var values = record.AllValues();
                    if (Is(record, "newtour"))
                    {
                        tour = new OmsiLineTour { Number = values.Count > 0 ? values[0] : string.Empty, VehicleGroup = values.Count > 1 ? values[1] : string.Empty };
                        if (values.Count > 2 && int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
                            tour.DayMask = days;
                        result.Add(tour);
                    }
                    else if (Is(record, "addtrip") && tour != null && values.Count >= 3 && int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var profile) &&
                             double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var departure))
                        tour.Trips.Add(new OmsiLineTrip { Trip = values[0], Profile = profile, DepartureMinutes = departure });
                }
                return result;
            }
        }

        /// <summary>The records of a tour: its [newtour] first, then its trips (those the reader takes).</summary>
        private List<OmsiRecord> TourRecords(int tour)
        {
            var found = -1;
            var result = new List<OmsiRecord>();
            foreach (var record in file.Records)
            {
                if (Is(record, "newtour"))
                {
                    found++;
                    if (found > tour)
                        break;
                }
                if (found == tour && (Is(record, "newtour") || Is(record, "addtrip") && IsReadableTrip(record)))
                    result.Add(record);
            }
            if (result.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(tour), "The line file has no tour " + tour + ".");
            return result;
        }

        private static bool IsReadableTrip(OmsiRecord record)
        {
            var values = record.AllValues();
            return values.Count >= 3 && int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                   double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        /// <summary>Changes the departure of a trip.</summary>
        public void SetDeparture(int tour, int trip, double minutes)
        {
            SetValue(TripRecord(tour, trip), 2, Minutes(minutes));
            RefreshComments();
        }

        /// <summary>Changes which profile (running time) of the trip's file a trip uses.</summary>
        public void SetProfile(int tour, int trip, int profile)
        {
            SetValue(TripRecord(tour, trip), 1, profile.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Lets a trip run another trip's file.</summary>
        public void SetTripName(int tour, int trip, string name)
        {
            SetValue(TripRecord(tour, trip), 0, OneLine(name));
        }

        /// <summary>Moves every trip of a tour by some minutes, none before midnight.</summary>
        public void ShiftTour(int tour, double deltaMinutes)
        {
            foreach (var record in TourRecords(tour).Skip(1))
                if (double.TryParse(Value(record, 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
                    SetValue(record, 2, Minutes(Math.Max(0.0, minutes + deltaMinutes)));
            RefreshComments();
        }

        /// <summary>Adds a trip to a tour, behind the last one that does not leave later, and returns its position in the tour.</summary>
        public int AddTrip(int tour, string trip, int profile, double minutes)
        {
            var records = TourRecords(tour);
            var position = 0;
            var after = records[0];
            for (var index = 1; index < records.Count; index++)
            {
                if (!double.TryParse(Value(records[index], 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var existing) || existing > minutes)
                    break;
                position = index;
                after = records[index];
            }
            var record = file.CreateRecord("addtrip", new string[0], 0);
            record.Trailer.Add(new OmsiLine(OneLine(trip), file.DefaultLineEnding));
            record.Trailer.Add(new OmsiLine(profile.ToString(CultureInfo.InvariantCulture), file.DefaultLineEnding));
            record.Trailer.Add(new OmsiLine(Minutes(minutes), file.DefaultLineEnding));
            record.Trailer.Add(new OmsiLine(string.Empty, file.DefaultLineEnding));
            file.InsertRecord(file.Records.IndexOf(after) + 1, record);
            RefreshComments();
            return position;
        }

        /// <summary>Adds the same trip every so many minutes from the first departure up to the last, both included; returns how many.</summary>
        public int AddSeries(int tour, string trip, int profile, double firstMinutes, double lastMinutes, double everyMinutes)
        {
            if (!(everyMinutes > 0.0))
                throw new ArgumentOutOfRangeException(nameof(everyMinutes), "The interval must be longer than 0 minutes.");
            var count = 0;
            for (var minutes = firstMinutes; minutes <= lastMinutes + 1e-6 && count < 1000; minutes += everyMinutes)
            {
                AddTrip(tour, trip, profile, minutes);
                count++;
            }
            return count;
        }

        public void RemoveTrip(int tour, int trip)
        {
            file.RemoveRecord(TripRecord(tour, trip));
            RefreshComments();
        }

        /// <summary>Renames a tour and sets its vehicle group; the name may carry the days it runs, as "Mo-Fr 1".</summary>
        public void SetTour(int tour, string number, string vehicleGroup)
        {
            var record = TourRecords(tour)[0];
            SetValue(record, 0, OneLine(number));
            SetValue(record, 1, OneLine(vehicleGroup));
        }

        /// <summary>Changes the days a tour runs; see <see cref="OmsiLineTour.DayMask"/>.</summary>
        public void SetTourDays(int tour, int dayMask)
        {
            SetValue(TourRecords(tour)[0], 2, Math.Max(0, Math.Min(1023, dayMask)).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Adds an empty tour at the end that runs on the days of the mask (all days by default).</summary>
        public int AddTour(string number, string vehicleGroup, int dayMask = 1023)
        {
            var tours = Tours;
            var record = file.CreateRecord("newtour", new[] { OneLine(number), OneLine(vehicleGroup), Math.Max(0, Math.Min(1023, dayMask)).ToString(CultureInfo.InvariantCulture) }, 0);
            record.Trailer.Add(new OmsiLine(string.Empty, file.DefaultLineEnding));
            if (file.Records.Count > 0)
            {
                var last = file.Records[file.Records.Count - 1];
                if (last.Trailer.Count == 0 || last.Trailer[last.Trailer.Count - 1].Text.Trim().Length > 0)
                    last.Trailer.Add(new OmsiLine(string.Empty, file.DefaultLineEnding));
                last.Trailer.Add(new OmsiLine(Dashes, file.DefaultLineEnding));
                last.Trailer.Add(new OmsiLine(string.Empty, file.DefaultLineEnding));
            }
            file.InsertRecord(file.Records.Count, record);
            return tours.Count;
        }

        /// <summary>Removes a tour with all its trips.</summary>
        public void RemoveTour(int tour)
        {
            foreach (var record in TourRecords(tour))
                file.RemoveRecord(record);
            RefreshComments();
        }

        private OmsiRecord TripRecord(int tour, int trip)
        {
            var records = TourRecords(tour);
            if (trip < 0 || trip + 1 >= records.Count)
                throw new ArgumentOutOfRangeException(nameof(trip), "The tour has no trip " + trip + ".");
            return records[trip + 1];
        }

        /// <summary>The comment before each trip says its departure as hours, minutes and seconds, and sits in the lines after the record before it.</summary>
        private void RefreshComments()
        {
            for (var index = 1; index < file.Records.Count; index++)
            {
                var record = file.Records[index];
                if (!Is(record, "addtrip") || !IsReadableTrip(record))
                    continue;
                var before = file.Records[index - 1];
                before.Trailer.RemoveAll(line => line.Text.Trim().StartsWith("Dep.:", StringComparison.Ordinal));
                if (before.Trailer.Count == 0 || before.Trailer[before.Trailer.Count - 1].Text.Trim().Length > 0)
                    before.Trailer.Add(new OmsiLine(string.Empty, file.DefaultLineEnding));
                double.TryParse(Value(record, 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes);
                var seconds = (long)Math.Round(minutes * 60.0);
                before.Trailer.Add(new OmsiLine("  Dep.: " + (seconds / 3600).ToString(CultureInfo.InvariantCulture) + ":" +
                                                (seconds / 60 % 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString(CultureInfo.InvariantCulture),
                    file.DefaultLineEnding));
            }
        }

        private static string Value(OmsiRecord record, int index)
        {
            var values = record.AllValues();
            return index < values.Count ? values[index] : string.Empty;
        }

        /// <summary>Sets a value line of a record, which are its fields and then the lines after them.</summary>
        private static void SetValue(OmsiRecord record, int index, string text)
        {
            if (index < record.Fields.Count)
                record.Fields[index].Text = text;
            else if (index - record.Fields.Count < record.Trailer.Count)
                record.Trailer[index - record.Fields.Count].Text = text;
            else
                throw new InvalidOperationException("The record has no value " + index + ".");
        }

        private static string Minutes(double minutes)
        {
            return minutes.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static bool Is(OmsiRecord record, string name)
        {
            return string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A trip file (<c>TTData\x.ttp</c>) opened for editing: the running times of its profiles.</summary>
    public sealed class OmsiTripFile
    {
        private readonly OmsiRecordFile file;

        private OmsiTripFile(OmsiRecordFile file)
        {
            this.file = file;
        }

        public static OmsiTripFile Parse(byte[] bytes)
        {
            return new OmsiTripFile(OmsiRecordFile.Parse(bytes, null));
        }

        public byte[] ToBytes()
        {
            return file.ToBytes();
        }

        /// <summary>The destination and the line the trip shows.</summary>
        public string Destination => Trip(1);

        public string Line => Trip(2);

        /// <summary>The profiles: a name and the running time from the first stop to the last in minutes.</summary>
        public List<KeyValuePair<string, double>> Profiles
        {
            get
            {
                var result = new List<KeyValuePair<string, double>>();
                foreach (var record in ProfileRecords())
                {
                    var values = record.AllValues();
                    if (values.Count >= 2 && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
                        result.Add(new KeyValuePair<string, double>(values[0], minutes));
                }
                return result;
            }
        }

        /// <summary>Changes the running time of a profile.</summary>
        public void SetRunningMinutes(int profile, double minutes)
        {
            var records = ProfileRecords();
            if (profile < 0 || profile >= records.Count)
                throw new ArgumentOutOfRangeException(nameof(profile), "The trip has no profile " + profile + ".");
            var record = records[profile];
            var text = minutes.ToString("0.000", CultureInfo.InvariantCulture);
            if (record.Fields.Count > 1)
                record.Fields[1].Text = text;
            else if (record.Fields.Count == 0 && record.Trailer.Count > 1)
                record.Trailer[1].Text = text;
            else
                throw new InvalidOperationException("The profile has no running time.");
        }

        private List<OmsiRecord> ProfileRecords()
        {
            return file.Records.Where(record => string.Equals(record.Name, "profile", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private string Trip(int index)
        {
            var record = file.Records.FirstOrDefault(candidate => string.Equals(candidate.Name, "trip", StringComparison.OrdinalIgnoreCase));
            var values = record?.AllValues();
            return values != null && index < values.Count ? values[index] : string.Empty;
        }
    }
}
