using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A stop of <c>Busstops.cfg</c>: its name and the object in a tile that marks it.</summary>
    public sealed class OmsiBusStop
    {
        public string Name { get; internal set; }

        /// <summary>Position of the tile in the [map] list of global.cfg, from 0.</summary>
        public int TileIndex { get; internal set; }

        public int ObjectId { get; internal set; }
    }

    /// <summary>A driven path of an object or spline, as station links and tracks list them.</summary>
    public sealed class OmsiPathEntry
    {
        public int ElementId { get; internal set; }

        /// <summary>The [path] of the element's .sco or .sli, from 0.</summary>
        public int PathIndex { get; internal set; }

        public int TileIndex { get; internal set; }
        public double LengthMeters { get; internal set; }
    }

    /// <summary>The route between two stops in <c>StnLinks.cfg</c>, over the paths of its entries.</summary>
    public sealed class OmsiStationLink
    {
        public double LengthMeters { get; internal set; }
        public int FromStopId { get; internal set; }
        public int ToStopId { get; internal set; }

        /// <summary>Where the stops lie along the paths of their entries.</summary>
        public double FromPosition { get; internal set; }

        public double ToPosition { get; internal set; }

        /// <summary>The entries the stops lie on.</summary>
        public int FromEntry { get; internal set; }

        public int ToEntry { get; internal set; }
        public List<OmsiPathEntry> Entries { get; } = new List<OmsiPathEntry>();
    }

    /// <summary>A track of a <c>.ttr</c> file: the paths a trip with its own route drives.</summary>
    public sealed class OmsiTrack
    {
        public string Name { get; internal set; }
        public List<OmsiPathEntry> Entries { get; } = new List<OmsiPathEntry>();
    }

    /// <summary>A stop of a trip: by its object, with the track entry it lies on for trips with their own track.</summary>
    public sealed class OmsiTripStop
    {
        public int ObjectId { get; internal set; }

        /// <summary>Entry of the trip's track, from 1, for the older [station] form; 0 for [station_typ2].</summary>
        public int TrackEntry { get; internal set; }

        public int TileIndex { get; internal set; } = -1;
        public string Name { get; internal set; }
    }

    /// <summary>A trip of a <c>.ttp</c> file: track, destination, line, stops and timing profiles.</summary>
    public sealed class OmsiTrip
    {
        public string Name { get; internal set; }

        /// <summary>The <c>.ttr</c> it drives, or empty when it drives over the station links.</summary>
        public string Track { get; internal set; }

        public string Destination { get; internal set; }
        public string Line { get; internal set; }
        public List<OmsiTripStop> Stops { get; } = new List<OmsiTripStop>();

        /// <summary>Profile names with their running time in minutes.</summary>
        public List<KeyValuePair<string, double>> Profiles { get; } = new List<KeyValuePair<string, double>>();
    }

    /// <summary>A trip a tour runs: the trip, its profile and the departure in minutes after midnight.</summary>
    public sealed class OmsiTourTrip
    {
        public string Trip { get; internal set; }
        public int Profile { get; internal set; }
        public double DepartureMinutes { get; internal set; }
    }

    public sealed class OmsiTour
    {
        public string Number { get; internal set; }
        public string VehicleGroup { get; internal set; }
        public List<OmsiTourTrip> Trips { get; } = new List<OmsiTourTrip>();
    }

    /// <summary>A line of a <c>.ttl</c> file with its tours.</summary>
    public sealed class OmsiTimetableLine
    {
        public string Name { get; internal set; }
        public List<OmsiTour> Tours { get; } = new List<OmsiTour>();
    }

    /// <summary>A finding of the timetable check.</summary>
    public sealed class OmsiTimetableProblem
    {
        public OmsiTimetableProblem(string file, string message)
        {
            File = file;
            Message = message;
        }

        /// <summary>The TTData file the finding concerns.</summary>
        public string File { get; }

        public string Message { get; }

        public override string ToString()
        {
            return File + ": " + Message;
        }
    }

    /// <summary>
    /// The timetable data of a map (<c>TTData</c>), read without changing anything: stops, station links, tracks, trips
    /// and lines. The formats are described in Documentation/TimetableChronoInventory.md.
    /// </summary>
    public sealed class OmsiTimetable
    {
        public List<OmsiBusStop> BusStops { get; } = new List<OmsiBusStop>();
        public List<OmsiStationLink> StationLinks { get; } = new List<OmsiStationLink>();
        public Dictionary<string, OmsiTrack> Tracks { get; } = new Dictionary<string, OmsiTrack>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, OmsiTrip> Trips { get; } = new Dictionary<string, OmsiTrip>(StringComparer.OrdinalIgnoreCase);
        public List<OmsiTimetableLine> Lines { get; } = new List<OmsiTimetableLine>();

        /// <summary>Files that could not be read.</summary>
        public List<OmsiTimetableProblem> ReadProblems { get; } = new List<OmsiTimetableProblem>();

        public bool IsEmpty => BusStops.Count == 0 && StationLinks.Count == 0 && Tracks.Count == 0 && Trips.Count == 0 && Lines.Count == 0;

        /// <summary>Reads the timetable files of a folder, by default the map's <c>TTData</c>; a missing folder gives an empty timetable.</summary>
        public static OmsiTimetable Read(IOmsiFileSource source, string folder = "TTData")
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var timetable = new OmsiTimetable();
            foreach (var path in source.EnumerateFiles(folder, "*", false))
            {
                var name = Path.GetFileName(path);
                var extension = Path.GetExtension(path).ToLowerInvariant();
                try
                {
                    if (string.Equals(name, "Busstops.cfg", StringComparison.OrdinalIgnoreCase))
                        timetable.ReadBusStops(source.ReadAllBytes(path));
                    else if (string.Equals(name, "StnLinks.cfg", StringComparison.OrdinalIgnoreCase))
                        timetable.ReadStationLinks(source.ReadAllBytes(path));
                    else if (extension == ".ttr")
                        timetable.Tracks[Path.GetFileNameWithoutExtension(path)] = ReadTrack(source.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
                    else if (extension == ".ttp")
                        timetable.Trips[Path.GetFileNameWithoutExtension(path)] = ReadTrip(source.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
                    else if (extension == ".ttl")
                        timetable.Lines.Add(ReadLine(source.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path)));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    timetable.ReadProblems.Add(new OmsiTimetableProblem(path, "could not be read: " + exception.Message));
                }
            }
            timetable.Lines.Sort((first, second) => string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));
            return timetable;
        }

        /// <summary>
        /// The timetable of a day: the base timetable with the timetables of the chrono variants active then, in their
        /// order. Stops and station links of a variant add to the earlier ones; a track, trip or line file with the name of
        /// an earlier one replaces it; lines a variant switches off are dropped.
        /// </summary>
        public static OmsiTimetable ReadForVariants(IOmsiFileSource source, IEnumerable<OmsiChronoVariant> variants)
        {
            var timetable = Read(source);
            foreach (var variant in variants)
            {
                var extra = Read(source, Path.Combine(variant.Folder, "TTData"));
                timetable.BusStops.AddRange(extra.BusStops);
                timetable.StationLinks.AddRange(extra.StationLinks);
                foreach (var pair in extra.Tracks)
                    timetable.Tracks[pair.Key] = pair.Value;
                foreach (var pair in extra.Trips)
                    timetable.Trips[pair.Key] = pair.Value;
                foreach (var line in extra.Lines)
                {
                    timetable.Lines.RemoveAll(existing => string.Equals(existing.Name, line.Name, StringComparison.OrdinalIgnoreCase));
                    timetable.Lines.Add(line);
                }
                timetable.ReadProblems.AddRange(extra.ReadProblems);
                foreach (var name in variant.DeactivatedLines)
                    timetable.Lines.RemoveAll(line => string.Equals(line.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            timetable.Lines.Sort((first, second) => string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));
            return timetable;
        }

        private void ReadBusStops(byte[] bytes)
        {
            foreach (var record in OmsiRecordFile.Parse(bytes, null).Records)
            {
                if (!Is(record, "busstop"))
                    continue;
                var values = record.AllValues();
                if (values.Count >= 3 && TryInt(values[1], out var tile) && TryInt(values[2], out var id))
                    BusStops.Add(new OmsiBusStop { Name = values[0], TileIndex = tile, ObjectId = id });
            }
        }

        private void ReadStationLinks(byte[] bytes)
        {
            OmsiStationLink link = null;
            foreach (var record in OmsiRecordFile.Parse(bytes, null).Records)
            {
                var values = record.AllValues();
                if (Is(record, "stnlink"))
                {
                    link = null;
                    if (values.Count >= 9 && TryDouble(values[0], out var length) && TryInt(values[1], out var from) && TryInt(values[2], out var to) &&
                        TryDouble(values[5], out var fromPosition) && TryDouble(values[6], out var toPosition) && TryInt(values[7], out var fromEntry) &&
                        TryInt(values[8], out var toEntry))
                    {
                        link = new OmsiStationLink
                        {
                            LengthMeters = length, FromStopId = from, ToStopId = to, FromPosition = fromPosition, ToPosition = toPosition,
                            FromEntry = fromEntry, ToEntry = toEntry
                        };
                        StationLinks.Add(link);
                    }
                }
                else if (Is(record, "stnlink_entry") && link != null && TryPathEntry(values, false, out var entry))
                {
                    link.Entries.Add(entry);
                }
            }
        }

        private static OmsiTrack ReadTrack(byte[] bytes, string name)
        {
            var track = new OmsiTrack { Name = name };
            foreach (var record in OmsiRecordFile.Parse(bytes, null).Records)
                if (Is(record, "track_entry") && TryPathEntry(record.AllValues(), true, out var entry))
                    track.Entries.Add(entry);
            return track;
        }

        /// <param name="tileWideNumber">Tracks put the tile-wide path number before the length.</param>
        private static bool TryPathEntry(List<string> values, bool tileWideNumber, out OmsiPathEntry entry)
        {
            entry = null;
            var lengthIndex = tileWideNumber ? 4 : 3;
            if (values.Count <= lengthIndex || !TryInt(values[0], out var element) || !TryInt(values[1], out var path) || !TryInt(values[2], out var tile) ||
                !TryDouble(values[lengthIndex], out var length))
                return false;
            entry = new OmsiPathEntry { ElementId = element, PathIndex = path, TileIndex = tile, LengthMeters = length };
            return true;
        }

        private static OmsiTrip ReadTrip(byte[] bytes, string name)
        {
            var trip = new OmsiTrip { Name = name, Track = string.Empty, Destination = string.Empty, Line = string.Empty };
            foreach (var record in OmsiRecordFile.Parse(bytes, null).Records)
            {
                var values = record.AllValues();
                if (Is(record, "trip"))
                {
                    trip.Track = values.Count > 0 ? values[0] : string.Empty;
                    trip.Destination = values.Count > 1 ? values[1] : string.Empty;
                    trip.Line = values.Count > 2 ? values[2] : string.Empty;
                }
                else if (Is(record, "station_typ2") && values.Count > 0 && TryInt(values[0], out var id))
                {
                    trip.Stops.Add(new OmsiTripStop { ObjectId = id });
                }
                else if (Is(record, "station") && values.Count >= 4 && TryInt(values[0], out var stationId) && TryInt(values[1], out var entry) &&
                         TryInt(values[3], out var tile))
                {
                    trip.Stops.Add(new OmsiTripStop { ObjectId = stationId, TrackEntry = entry, Name = values[2], TileIndex = tile });
                }
                else if (Is(record, "profile") && values.Count >= 2 && TryDouble(values[1], out var minutes))
                {
                    trip.Profiles.Add(new KeyValuePair<string, double>(values[0], minutes));
                }
            }
            return trip;
        }

        private static OmsiTimetableLine ReadLine(byte[] bytes, string name)
        {
            var line = new OmsiTimetableLine { Name = name };
            OmsiTour tour = null;
            foreach (var record in OmsiRecordFile.Parse(bytes, null).Records)
            {
                var values = record.AllValues();
                if (Is(record, "newtour"))
                {
                    tour = new OmsiTour { Number = values.Count > 0 ? values[0] : string.Empty, VehicleGroup = values.Count > 1 ? values[1] : string.Empty };
                    line.Tours.Add(tour);
                }
                else if (Is(record, "addtrip") && tour != null && values.Count >= 3 && TryInt(values[1], out var profile) &&
                         TryDouble(values[2], out var departure))
                {
                    tour.Trips.Add(new OmsiTourTrip { Trip = values[0], Profile = profile, DepartureMinutes = departure });
                }
            }
            return line;
        }

        private static bool Is(OmsiRecord record, string name)
        {
            return string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryInt(string value, out int result)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }

    /// <summary>What the timetable check counted and found.</summary>
    public sealed class OmsiTimetableReport
    {
        public int BusStopCount { get; internal set; }
        public int StationLinkCount { get; internal set; }
        public int TrackCount { get; internal set; }
        public int TripCount { get; internal set; }
        public int LineCount { get; internal set; }
        public int TourCount { get; internal set; }
        public int ScheduledTripCount { get; internal set; }
        public List<OmsiTimetableProblem> Problems { get; } = new List<OmsiTimetableProblem>();
    }

    /// <summary>
    /// Checks the cross references of a map's timetable against its tiles and against itself, as found in the format
    /// study: stops on existing objects, station links between known stops over existing paths and with a length that
    /// matches their entries, tracks over existing paths with spline lengths that match, trips over existing tracks
    /// and station links, and lines that run existing trips and profiles.
    /// </summary>
    public static class OmsiTimetableCheck
    {
        /// <param name="chronoIds">
        /// IDs that chrono variants add; a reference to one of them is reported as depending on a variant rather than as
        /// pointing at nothing. Null when the map has no variants or they apply already.
        /// </param>
        public static OmsiTimetableReport Check(OmsiTimetable timetable, OmsiMapSummary summary, ISet<int> chronoIds = null)
        {
            if (timetable == null)
                throw new ArgumentNullException(nameof(timetable));
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            var report = new OmsiTimetableReport
            {
                BusStopCount = timetable.BusStops.Count, StationLinkCount = timetable.StationLinks.Count, TrackCount = timetable.Tracks.Count,
                TripCount = timetable.Trips.Count, LineCount = timetable.Lines.Count
            };
            report.Problems.AddRange(timetable.ReadProblems);
            var elements = ElementsByTile(summary);
            string Missing(int id, string tile) => chronoIds != null && chronoIds.Contains(id)
                ? "which only a chrono variant adds"
                : "which " + tile + " does not contain";

            var stops = new HashSet<int>();
            foreach (var stop in timetable.BusStops)
            {
                stops.Add(stop.ObjectId);
                if (!Contains(elements, stop.TileIndex, stop.ObjectId, out var tile))
                    report.Problems.Add(new OmsiTimetableProblem("Busstops.cfg", "stop \"" + stop.Name + "\" refers to object " + stop.ObjectId +
                                                                               ", " + Missing(stop.ObjectId, tile)));
            }

            var links = new HashSet<long>();
            foreach (var link in timetable.StationLinks)
            {
                var where = "link " + link.FromStopId + " → " + link.ToStopId;
                if (!stops.Contains(link.FromStopId) || !stops.Contains(link.ToStopId))
                    report.Problems.Add(new OmsiTimetableProblem("StnLinks.cfg", where + " starts or ends at an object that is no stop"));
                if (!links.Add(Pair(link.FromStopId, link.ToStopId)))
                    report.Problems.Add(new OmsiTimetableProblem("StnLinks.cfg", where + " exists twice"));
                CheckEntries(link.Entries, elements, "StnLinks.cfg", where, report, Missing);
                var length = LinkLength(link);
                if (length.HasValue && Math.Abs(length.Value - link.LengthMeters) > Tolerance(link.LengthMeters))
                    report.Problems.Add(new OmsiTimetableProblem("StnLinks.cfg", where + " is " + Format(link.LengthMeters) + " m long, but its entries give " +
                                                                               Format(length.Value) + " m"));
            }

            var splines = SplinesById(summary);
            foreach (var track in timetable.Tracks.Values)
            {
                var file = track.Name + ".ttr";
                CheckEntries(track.Entries, elements, file, "track", report, Missing);
                foreach (var entry in track.Entries)
                    if (splines.TryGetValue(entry.ElementId, out var spline) && Math.Abs(spline.LengthMeters - entry.LengthMeters) > Tolerance(spline.LengthMeters))
                        report.Problems.Add(new OmsiTimetableProblem(file, "entry on spline " + entry.ElementId + " is " + Format(entry.LengthMeters) +
                                                                           " m long, the spline " + Format(spline.LengthMeters) + " m"));
            }

            foreach (var trip in timetable.Trips.Values)
            {
                var file = trip.Name + ".ttp";
                if (trip.Track.Length > 0 && !timetable.Tracks.ContainsKey(trip.Track))
                    report.Problems.Add(new OmsiTimetableProblem(file, "drives track \"" + trip.Track + "\", which has no .ttr file"));
                if (trip.Profiles.Count == 0)
                    report.Problems.Add(new OmsiTimetableProblem(file, "has no [profile]"));
                OmsiTripStop previous = null;
                foreach (var stop in trip.Stops)
                {
                    if (stop.TrackEntry == 0)
                    {
                        if (!stops.Contains(stop.ObjectId))
                            report.Problems.Add(new OmsiTimetableProblem(file, "stops at object " + stop.ObjectId + ", which is no stop of Busstops.cfg"));
                        if (previous != null && previous.TrackEntry == 0 && !links.Contains(Pair(previous.ObjectId, stop.ObjectId)))
                            report.Problems.Add(new OmsiTimetableProblem(file, "has no station link from " + previous.ObjectId + " to " + stop.ObjectId));
                    }
                    else if (!Contains(elements, stop.TileIndex, stop.ObjectId, out var tile))
                    {
                        report.Problems.Add(new OmsiTimetableProblem(file, "stops at object " + stop.ObjectId + ", " + Missing(stop.ObjectId, tile)));
                    }
                    previous = stop;
                }
            }

            foreach (var line in timetable.Lines)
            {
                var file = line.Name + ".ttl";
                report.TourCount += line.Tours.Count;
                var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tour in line.Tours)
                    foreach (var tourTrip in tour.Trips)
                    {
                        report.ScheduledTripCount++;
                        if (!timetable.Trips.TryGetValue(tourTrip.Trip, out var trip))
                        {
                            if (missing.Add(tourTrip.Trip))
                                report.Problems.Add(new OmsiTimetableProblem(file, "runs trip \"" + tourTrip.Trip + "\", which has no .ttp file"));
                        }
                        else if (tourTrip.Profile < 0 || tourTrip.Profile >= trip.Profiles.Count)
                        {
                            report.Problems.Add(new OmsiTimetableProblem(file, "tour " + tour.Number + " runs \"" + tourTrip.Trip + "\" with profile " +
                                                                               tourTrip.Profile + ", which the trip does not have"));
                        }
                    }
            }
            return report;
        }

        /// <summary>
        /// The distance between the stops of a link: the rest of the start entry, the entries between and the start of the
        /// end entry; null when the entry indices do not fit.
        /// </summary>
        public static double? LinkLength(OmsiStationLink link)
        {
            if (link.FromEntry < 0 || link.ToEntry >= link.Entries.Count || link.FromEntry > link.ToEntry)
                return null;
            if (link.FromEntry == link.ToEntry)
                return link.ToPosition - link.FromPosition;
            var length = link.Entries[link.FromEntry].LengthMeters - link.FromPosition + link.ToPosition;
            for (var index = link.FromEntry + 1; index < link.ToEntry; index++)
                length += link.Entries[index].LengthMeters;
            return length;
        }

        /// <summary>OMSI writes lengths with three significant digits, so a value is only known to half its last digit.</summary>
        private static double Tolerance(double length)
        {
            return Math.Max(0.6, Math.Abs(length) * 0.006);
        }

        private static void CheckEntries(List<OmsiPathEntry> entries, Dictionary<int, HashSet<int>> elements, string file, string where,
            OmsiTimetableReport report, Func<int, string, string> missing)
        {
            foreach (var entry in entries)
                if (!Contains(elements, entry.TileIndex, entry.ElementId, out var tile))
                    report.Problems.Add(new OmsiTimetableProblem(file, where + " drives element " + entry.ElementId + ", " + missing(entry.ElementId, tile)));
        }

        /// <summary>The IDs of objects, attached objects, rows along splines and splines, by tile index.</summary>
        private static Dictionary<int, HashSet<int>> ElementsByTile(OmsiMapSummary summary)
        {
            var result = new Dictionary<int, HashSet<int>>();
            for (var index = 0; index < summary.Tiles.Count; index++)
            {
                var tile = summary.Tiles[index];
                var ids = new HashSet<int>();
                foreach (var item in tile.Objects)
                    ids.Add(item.Id);
                foreach (var item in tile.AttachedObjects)
                    ids.Add(item.Id);
                foreach (var item in tile.SplineAttachments)
                    ids.Add(item.Id);
                foreach (var item in tile.Splines)
                    ids.Add(item.Id);
                result[index] = ids;
            }
            return result;
        }

        private static bool Contains(Dictionary<int, HashSet<int>> elements, int tileIndex, int id, out string tile)
        {
            if (!elements.TryGetValue(tileIndex, out var ids))
            {
                tile = "tile index " + tileIndex + " (not in the map)";
                return false;
            }
            tile = "tile index " + tileIndex;
            return ids.Contains(id);
        }

        private static Dictionary<int, OmsiSplineRecord> SplinesById(OmsiMapSummary summary)
        {
            var result = new Dictionary<int, OmsiSplineRecord>();
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                    result[spline.Id] = spline;
            return result;
        }

        private static long Pair(int from, int to)
        {
            return ((long)from << 32) | (uint)to;
        }

        private static string Format(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
