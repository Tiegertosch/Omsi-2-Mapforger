using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A trip of a new line: its name, where it goes, its running time and the way found for it.</summary>
    public sealed class OmsiTripSpec
    {
        public string Name { get; set; }
        public string Destination { get; set; }
        public string ProfileName { get; set; } = "standard";
        public double RunningMinutes { get; set; } = 10.0;
        public OmsiTripPlan Plan { get; set; }
    }

    public sealed partial class OmsiMapSession
    {
        private const string BusStopsFile = "TTData\\Busstops.cfg";
        private const string StationLinksFile = "TTData\\StnLinks.cfg";

        /// <summary>The stops of the map's Busstops.cfg with the places of their objects, for choosing the stops of a trip.</summary>
        public List<OmsiTripStopChoice> ReadStopChoices(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var result = new List<OmsiTripStopChoice>();
            var timetable = OmsiTimetable.Read(RequireWorkspace());
            foreach (var stop in timetable.BusStops)
            {
                if (stop.TileIndex < 0 || stop.TileIndex >= summary.Tiles.Count)
                    continue;
                var tile = summary.Tiles[stop.TileIndex];
                var record = tile.Objects.Find(item => item.Id == stop.ObjectId);
                if (record == null)
                    continue;
                result.Add(new OmsiTripStopChoice
                {
                    Name = stop.Name, TileIndex = stop.TileIndex, ObjectId = stop.ObjectId,
                    World = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, record.LocalX, record.LocalY, summary.TileSizeMeters)
                });
            }
            return result;
        }

        /// <summary>
        /// The objects of the map that are bus stops, as OMSI's editor knows them (the object <c>bus_stop.sco</c>), and are not in Busstops.cfg yet, to make
        /// stops of for a trip; their names are made of the file and the ID until the user gives one.
        /// </summary>
        public List<OmsiTripStopChoice> FindStopObjects(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var known = new HashSet<int>(OmsiTimetable.Read(RequireWorkspace()).BusStops.Select(stop => stop.ObjectId));
            var result = new List<OmsiTripStopChoice>();
            for (var index = 0; index < summary.Tiles.Count; index++)
            {
                var tile = summary.Tiles[index];
                if (!tile.IsAvailable)
                    continue;
                foreach (var record in tile.Objects)
                {
                    var file = System.IO.Path.GetFileNameWithoutExtension((record.AssetPath ?? string.Empty).Replace('\\', '/'));
                    if (known.Contains(record.Id) || !string.Equals(file, "bus_stop", StringComparison.OrdinalIgnoreCase))
                        continue;
                    result.Add(new OmsiTripStopChoice
                    {
                        Name = record.Labels.Count > 0 && !string.IsNullOrWhiteSpace(record.Labels[0]) ? record.Labels[0].Trim()
                            : "Stop " + record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), TileIndex = index, ObjectId = record.Id, IsNew = true,
                        World = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, record.LocalX, record.LocalY, summary.TileSizeMeters)
                    });
                }
            }
            return result;
        }

        /// <summary>A stop made of a tile's object that is not in Busstops.cfg yet; null when the tile or object is not found.</summary>
        public OmsiTripStopChoice NewStopChoice(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record, string name)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var index = tile == null ? -1 : summary.Tiles.IndexOf(tile);
            if (index < 0 || record == null)
                return null;
            return new OmsiTripStopChoice
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Stop" : name.Trim(), TileIndex = index, ObjectId = record.Id, IsNew = true,
                World = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, record.LocalX, record.LocalY, summary.TileSizeMeters)
            };
        }

        /// <summary>True for the object of a bus stop: a <c>bus_stop.sco</c>, or an object the map's Busstops.cfg lists.</summary>
        public bool IsBusStop(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record)
        {
            if (summary == null || tile == null || record == null)
                return false;
            var file = System.IO.Path.GetFileNameWithoutExtension((record.AssetPath ?? string.Empty).Replace('\\', '/'));
            return string.Equals(file, "bus_stop", StringComparison.OrdinalIgnoreCase) || BusStopName(summary, tile, record) != null;
        }

        /// <summary>
        /// The labels of a bus stop with its name set: the first label is the stop's name (OMSI's editor writes it there, and Busstops.cfg takes it from it).
        /// An object without labels gets those of a plain stop (name, three numbers for the passengers and the length of the stop, empty lines).
        /// </summary>
        public static List<string> StopLabels(IReadOnlyList<string> current, string name)
        {
            var labels = current != null && current.Count > 0
                ? new List<string>(current)
                : new List<string> { string.Empty, "10", "0", "10", string.Empty, string.Empty, string.Empty, string.Empty };
            labels[0] = (name ?? string.Empty).Trim();
            return labels;
        }

        /// <summary>The name Busstops.cfg gives the stop of an object, or null when it does not list it.</summary>
        public string BusStopName(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record)
        {
            var index = summary.Tiles.IndexOf(tile);
            if (index < 0 || record == null)
                return null;
            var stop = OmsiTimetable.Read(RequireWorkspace()).BusStops.Find(each => each.TileIndex == index && each.ObjectId == record.Id);
            return stop?.Name;
        }

        /// <summary>Gives the stop of an object its name in Busstops.cfg (making it a stop of the timetable if it is none); one undoable step, or null when nothing changes.</summary>
        public OmsiTerrainChange SetBusStopName(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record, string name)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var index = tile == null ? -1 : summary.Tiles.IndexOf(tile);
            if (index < 0 || record == null)
                throw new ArgumentException("The object is not on a tile of the map.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A stop needs a name.", nameof(name));
            var workspace = RequireWorkspace();
            var before = workspace.FileExists(BusStopsFile) ? workspace.ReadAllBytes(BusStopsFile) : null;
            var after = OmsiTimetableWriter.SetBusStopName(before, index, record.Id, name.Trim());
            if (before != null && SameBytes(before, after))
                return null;
            var change = new OmsiTerrainChange { Painted = true, TimetableFiles = true };
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = BusStopsFile, Before = before, After = after });
            ApplyFiles(change.Files, false);
            return change;
        }

        /// <summary>True for a map with left-hand traffic (the <c>[LHT]</c> flag of global.cfg), where buses stop on the left.</summary>
        public bool HasLeftHandTraffic()
        {
            var config = OmsiRecordFile.Parse(RequireWorkspace().ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            return config.Records.Any(record => string.Equals(record.Name, "LHT", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Every stop object of the map for the line tool: the stops of Busstops.cfg with their names, and the other bus_stop.sco objects as new stops.</summary>
        public List<OmsiTripStopChoice> ListStopObjects(OmsiMapSummary summary)
        {
            var result = ReadStopChoices(summary);
            result.AddRange(FindStopObjects(summary));
            return result;
        }

        /// <summary>The way over the lanes for a sequence of stops (and waypoints), with the timetable and traffic side already read.</summary>
        public OmsiTripPlan PlanTrip(OmsiMapSummary summary, OmsiTimetable timetable, IList<OmsiTripStopChoice> stops, OmsiPathNetwork network, bool rightHandTraffic)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            return OmsiTripPlanner.Plan(stops, timetable, network, (x, y) => summary.Tiles.FindIndex(tile => tile.X == x && tile.Y == y), rightHandTraffic,
                index => index >= 0 && index < summary.Tiles.Count ? new[] { summary.Tiles[index].X, summary.Tiles[index].Y } : null);
        }

        /// <summary>The ways between the stops of a trip, found on the vehicle lanes of the map or taken from the station links it has.</summary>
        public OmsiTripPlan PlanTrip(OmsiMapSummary summary, string installationRoot, IList<OmsiTripStopChoice> stops, OmsiPathNetwork network = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            network = network ?? OmsiPathNetwork.ForMap(summary, installationRoot);
            var timetable = OmsiTimetable.Read(RequireWorkspace());
            return OmsiTripPlanner.Plan(stops, timetable, network, (x, y) => summary.Tiles.FindIndex(tile => tile.X == x && tile.Y == y), !HasLeftHandTraffic(),
                index => index >= 0 && index < summary.Tiles.Count ? new[] { summary.Tiles[index].X, summary.Tiles[index].Y } : null);
        }

        /// <summary>
        /// Writes the trips of a line: each trip file, the station links the timetable lacks (a link a leg's waypoints replace is replaced), the stops
        /// they add, and the line file when the map has no line of that name. Everything is computed first and nothing is written when a plan has
        /// problems or a trip's file exists. One step that undoes all of it; null when there is nothing to write.
        /// </summary>
        public OmsiTerrainChange CreateLine(string lineName, string vehicleGroup, IList<OmsiTripSpec> trips)
        {
            if (trips == null)
                throw new ArgumentNullException(nameof(trips));
            var workspace = RequireWorkspace();
            var files = new List<KeyValuePair<string, byte[]>>();
            var legs = new List<OmsiTripLeg>();
            var stops = new List<OmsiTripStopChoice>();
            var known = OmsiTimetable.Read(workspace).BusStops.Select(stop => stop.ObjectId).ToList();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var trip in trips)
            {
                if (trip.Plan == null || !trip.Plan.IsValid)
                    throw new InvalidOperationException("The trip " + trip.Name + " has problems: " + string.Join(" ", trip.Plan?.Problems ?? new List<string>()));
                var tripPath = TimetablePath(trip.Name, ".ttp");
                if (!names.Add(trip.Name) || workspace.FileExists(tripPath))
                    throw new InvalidOperationException("The map has a trip named " + trip.Name + " already.");
                var stopIds = new List<int> { trip.Plan.Legs[0].From.ObjectId };
                stopIds.AddRange(trip.Plan.Legs.Select(leg => leg.To.ObjectId));
                files.Add(new KeyValuePair<string, byte[]>(tripPath, OmsiTimetableWriter.CreateTrip(trip.Destination, lineName, stopIds, trip.ProfileName, trip.RunningMinutes)));
                foreach (var leg in trip.Plan.Legs)
                {
                    foreach (var stop in new[] { leg.From, leg.To })
                        if (!known.Contains(stop.ObjectId) && !stops.Exists(each => each.ObjectId == stop.ObjectId))
                            stops.Add(stop);
                    if (!legs.Exists(each => each.From.ObjectId == leg.From.ObjectId && each.To.ObjectId == leg.To.ObjectId))
                        legs.Add(leg);
                }
            }
            if (stops.Count > 0)
                files.Add(new KeyValuePair<string, byte[]>(BusStopsFile, OmsiTimetableWriter.AppendBusStops(
                    workspace.FileExists(BusStopsFile) ? workspace.ReadAllBytes(BusStopsFile) : null, stops)));
            if (legs.Any(leg => !leg.Existing))
                files.Add(new KeyValuePair<string, byte[]>(StationLinksFile, OmsiTimetableWriter.AppendStationLinks(
                    workspace.FileExists(StationLinksFile) ? workspace.ReadAllBytes(StationLinksFile) : null, legs)));
            var linePath = TimetablePath(lineName, ".ttl");
            if (!workspace.FileExists(linePath))
                files.Add(new KeyValuePair<string, byte[]>(linePath, OmsiTimetableWriter.CreateLine(vehicleGroup)));
            if (files.Count == 0)
                return null;
            var change = new OmsiTerrainChange { Painted = true, TimetableFiles = true };
            foreach (var file in files)
                change.Files.Add(new OmsiObjectChange.FileState
                {
                    RelativePath = file.Key, Before = workspace.FileExists(file.Key) ? workspace.ReadAllBytes(file.Key) : null, After = file.Value
                });
            ApplyFiles(change.Files, false);
            return change;
        }

        /// <summary>The names of the vehicle groups of the map's ailists.cfg (<c>[aigroup_2]</c> and the depot lists), which a tour names for its vehicles.</summary>
        public List<string> ReadAiGroups()
        {
            var workspace = RequireWorkspace();
            var result = new List<string>();
            if (!workspace.FileExists("ailists.cfg"))
                return result;
            foreach (var record in OmsiRecordFile.Parse(workspace.ReadAllBytes("ailists.cfg"), null).Records)
            {
                if (!string.Equals(record.Name, "aigroup_2", StringComparison.OrdinalIgnoreCase) && !string.Equals(record.Name, "aigroup_depot", StringComparison.OrdinalIgnoreCase))
                    continue;
                var values = record.AllValues();
                if (values.Count > 0 && values[0].Length > 0 && !result.Contains(values[0], StringComparer.OrdinalIgnoreCase))
                    result.Add(values[0]);
            }
            return result;
        }

        /// <summary>Makes a line file with one empty tour; fails when the map has a line of that name.</summary>
        public void CreateLineFile(string line, string vehicleGroup)
        {
            var workspace = RequireWorkspace();
            var path = TimetablePath(line, ".ttl");
            if (workspace.FileExists(path))
                throw new InvalidOperationException("The map has a line named " + line + " already.");
            workspace.Save(path, OmsiTimetableWriter.CreateLine(vehicleGroup));
        }
    }
}
