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

        // ---- stops ----

        private const string StopRecord = "station_typ2";
        private const string ArrivalRecord = "profile_man_arr_time";
        private const string DepartureRecord = "profile_man_dep_time";
        private const string StoppingRecord = "profile_otherstopping";

        /// <summary>True for a trip that runs on a track of its own (a <c>.ttr</c>) or lists its stops as <c>[station]</c> records; the stops of such a trip are not edited here.</summary>
        public bool HasTrack => Trip(0).Length > 0 || file.Records.Any(record => string.Equals(record.Name, "station", StringComparison.OrdinalIgnoreCase));

        private List<OmsiRecord> StopRecords()
        {
            return file.Records.Where(record => string.Equals(record.Name, StopRecord, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>The object IDs of the stops in the order the trip serves them.</summary>
        public List<int> StopIds
        {
            get
            {
                var result = new List<int>();
                foreach (var record in StopRecords())
                {
                    var values = record.AllValues();
                    result.Add(values.Count > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : -1);
                }
                return result;
            }
        }

        /// <summary>
        /// Puts other stops into the trip. What the profiles say of single stops (times, stopping) follows each stop that stays and goes with each that is
        /// dropped; the lines around the stops stay as they are.
        /// </summary>
        public void SetStops(IList<int> ids)
        {
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            if (ids.Count < 2)
                throw new ArgumentException("A trip needs at least two stops.", nameof(ids));
            if (HasTrack)
                throw new InvalidOperationException("The trip runs on a track of its own; its stops are not edited here.");
            var records = StopRecords();
            if (records.Count == 0)
                throw new InvalidOperationException("The trip lists no stops to replace.");
            var before = StopIds;
            // Where each old stop goes: the same object, counted in order of appearance.
            var map = new int?[before.Count];
            var used = new HashSet<int>();
            for (var index = 0; index < before.Count; index++)
                for (var target = 0; target < ids.Count; target++)
                    if (ids[target] == before[index] && used.Add(target))
                    {
                        map[index] = target;
                        break;
                    }
            RemapProfiles(map);

            var lastOld = records[records.Count - 1];
            var tail = lastOld.Trailer.Skip(1).ToList();
            var position = file.Records.IndexOf(lastOld) + 1;
            for (var index = 0; index < ids.Count; index++)
            {
                var text = ids[index].ToString(CultureInfo.InvariantCulture);
                if (index < records.Count)
                {
                    if (records[index].Trailer.Count == 0)
                        records[index].Trailer.Add(new OmsiLine(text, file.DefaultLineEnding));
                    else
                        records[index].Trailer[0].Text = text;
                }
                else
                {
                    var record = file.CreateRecord(StopRecord, new string[0], 0);
                    record.Trailer.Add(new OmsiLine(text, file.DefaultLineEnding));
                    record.Trailer.Add(new OmsiLine(string.Empty, file.DefaultLineEnding));
                    file.InsertRecord(position++, record);
                    records.Add(record);
                }
            }
            for (var index = records.Count - 1; index >= ids.Count; index--)
                file.RemoveRecord(records[index]);
            records.RemoveRange(ids.Count, records.Count - ids.Count);
            // What followed the last stop (the comment block above the profiles) follows the new last one.
            var newLast = records[records.Count - 1];
            if (!ReferenceEquals(newLast, lastOld))
            {
                if (records.Count > 0 && lastOld.Trailer.Count > 1 && file.Records.Contains(lastOld))
                    lastOld.Trailer.RemoveRange(1, lastOld.Trailer.Count - 1);
                if (file.Records.Contains(lastOld))
                    lastOld.Trailer.Add(new OmsiLine(string.Empty, file.DefaultLineEnding));
                var keep = newLast.Trailer.Count > 0 ? newLast.Trailer[0] : new OmsiLine(string.Empty, file.DefaultLineEnding);
                newLast.Trailer.Clear();
                newLast.Trailer.Add(keep);
                foreach (var line in tail)
                    newLast.Trailer.Add(line);
            }
        }

        /// <summary>Sets the place the bus is going to, the first of the lines under <c>[trip]</c> after the track.</summary>
        public void SetDestination(string destination)
        {
            var record = file.Records.FirstOrDefault(candidate => string.Equals(candidate.Name, "trip", StringComparison.OrdinalIgnoreCase));
            if (record == null)
                throw new InvalidOperationException("The trip file has no [trip].");
            var line = record.Fields.Count > 1 ? record.Fields[1] : record.Trailer.Count > 1 - record.Fields.Count ? record.Trailer[1 - record.Fields.Count] : null;
            if (line == null)
                throw new InvalidOperationException("The trip file has no destination line.");
            line.Text = OneLine(destination);
        }

        // ---- the times and stopping of single stops (per profile) ----

        /// <summary>How a profile times and stops at single stops; stops it does not mention follow from the running time and the lengths of the station links.</summary>
        public sealed class StopSettings
        {
            /// <summary>Minutes after the start the bus is at the stop (arrival, departure); a stop that has one is a time point the bus waits at.</summary>
            public Dictionary<int, double> Arrivals { get; } = new Dictionary<int, double>();
            public Dictionary<int, double> Departures { get; } = new Dictionary<int, double>();
            /// <summary>1 and 4 stop for whoever wants to get on or off, 2 is passed, 3 is served when the bus is more than 20 seconds early.</summary>
            public Dictionary<int, int> Stopping { get; } = new Dictionary<int, int>();
        }

        private IEnumerable<OmsiRecord> DetailRecords(int profile)
        {
            var records = ProfileRecords();
            if (profile < 0 || profile >= records.Count)
                throw new ArgumentOutOfRangeException(nameof(profile), "The trip has no profile " + profile + ".");
            for (var index = file.Records.IndexOf(records[profile]) + 1; index < file.Records.Count; index++)
            {
                var name = file.Records[index].Name;
                if (!name.StartsWith("profile_", StringComparison.OrdinalIgnoreCase))
                    yield break;
                yield return file.Records[index];
            }
        }

        private static bool IsDetail(OmsiRecord record, string name)
        {
            return string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase);
        }

        public StopSettings GetStopSettings(int profile)
        {
            var result = new StopSettings();
            foreach (var record in DetailRecords(profile))
            {
                var values = record.AllValues();
                if (values.Count < 2 || !int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stop))
                    continue;
                if (IsDetail(record, StoppingRecord))
                {
                    if (int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind))
                        result.Stopping[stop] = kind;
                }
                else if ((IsDetail(record, ArrivalRecord) || IsDetail(record, DepartureRecord)) &&
                         double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
                {
                    (IsDetail(record, ArrivalRecord) ? result.Arrivals : result.Departures)[stop] = minutes;
                }
            }
            return result;
        }

        /// <summary>Sets (or, with null, takes away) the minute the bus arrives at a stop after the trip began.</summary>
        public void SetArrival(int profile, int stop, double? minutes)
        {
            SetDetail(profile, ArrivalRecord, stop, minutes.HasValue ? minutes.Value.ToString("0.000", CultureInfo.InvariantCulture) : null);
        }

        /// <summary>Sets (or, with null, takes away) the minute the bus leaves a stop after the trip began.</summary>
        public void SetDeparture(int profile, int stop, double? minutes)
        {
            SetDetail(profile, DepartureRecord, stop, minutes.HasValue ? minutes.Value.ToString("0.000", CultureInfo.InvariantCulture) : null);
        }

        /// <summary>Sets (or, with null, takes away) how the bus stops at a stop; see <see cref="StopSettings.Stopping"/>.</summary>
        public void SetStopping(int profile, int stop, int? kind)
        {
            if (kind.HasValue && (kind.Value < 0 || kind.Value > 4))
                throw new ArgumentOutOfRangeException(nameof(kind), "Stopping is 0 to 4.");
            SetDetail(profile, StoppingRecord, stop, kind.HasValue ? kind.Value.ToString(CultureInfo.InvariantCulture) : null);
        }

        private void SetDetail(int profile, string name, int stop, string value)
        {
            if (stop < 0 || stop >= Math.Max(StopRecords().Count, StopIds.Count))
                throw new ArgumentOutOfRangeException(nameof(stop), "The trip has no stop " + stop + ".");
            var details = DetailRecords(profile).ToList();
            var existing = details.FirstOrDefault(record =>
            {
                var values = record.AllValues();
                return IsDetail(record, name) && values.Count > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index == stop;
            });
            if (value == null)
            {
                if (existing != null)
                    file.RemoveRecord(existing);
                return;
            }
            if (existing != null)
            {
                var line = existing.Fields.Count > 1 ? existing.Fields[1] : existing.Trailer.Count > 1 - existing.Fields.Count ? existing.Trailer[1 - existing.Fields.Count] : null;
                if (line == null)
                    throw new InvalidOperationException("The record has no value.");
                line.Text = value;
                return;
            }
            // New records go behind the others of their kind, in the order the editor writes them: arrivals, departures, stopping.
            var order = new[] { ArrivalRecord, DepartureRecord, StoppingRecord };
            var rank = Array.IndexOf(order, name);
            var profileRecord = ProfileRecords()[profile];
            var insertAt = file.Records.IndexOf(profileRecord) + 1;
            foreach (var record in details)
                if (Array.FindIndex(order, each => IsDetail(record, each)) <= rank)
                    insertAt = file.Records.IndexOf(record) + 1;
            var created = file.CreateRecord(name, new string[0], 0);
            created.Trailer.Add(new OmsiLine(stop.ToString(CultureInfo.InvariantCulture), file.DefaultLineEnding));
            created.Trailer.Add(new OmsiLine(value, file.DefaultLineEnding));
            created.Trailer.Add(new OmsiLine(string.Empty, file.DefaultLineEnding));
            file.InsertRecord(insertAt, created);
        }

        /// <summary>Gives the per-stop records of every profile their stop's new place; a record whose stop is gone goes.</summary>
        private void RemapProfiles(int?[] map)
        {
            foreach (var record in file.Records.ToList())
            {
                var kind = new[] { ArrivalRecord, DepartureRecord, StoppingRecord }.Any(name => IsDetail(record, name));
                if (!kind)
                    continue;
                var line = record.Fields.Count > 0 ? record.Fields[0] : record.Trailer.Count > 0 ? record.Trailer[0] : null;
                if (line == null || !int.TryParse(line.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                    continue;
                if (index < 0 || index >= map.Length || !map[index].HasValue)
                    file.RemoveRecord(record);
                else if (map[index].Value != index)
                    line.Text = map[index].Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private string Trip(int index)
        {
            var record = file.Records.FirstOrDefault(candidate => string.Equals(candidate.Name, "trip", StringComparison.OrdinalIgnoreCase));
            var values = record?.AllValues();
            return values != null && index < values.Count ? values[index] : string.Empty;
        }
    }
}
