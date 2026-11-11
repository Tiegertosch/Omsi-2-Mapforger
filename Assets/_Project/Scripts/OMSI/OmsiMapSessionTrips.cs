using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
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
                        Name = file + " " + record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), TileIndex = index, ObjectId = record.Id, IsNew = true,
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

        /// <summary>True for a map with left-hand traffic (the <c>[LHT]</c> flag of global.cfg), where buses stop on the left.</summary>
        public bool HasLeftHandTraffic()
        {
            var config = OmsiRecordFile.Parse(RequireWorkspace().ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            return config.Records.Any(record => string.Equals(record.Name, "LHT", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The ways between the stops of a trip, found on the vehicle lanes of the map or taken from the station links it has.</summary>
        public OmsiTripPlan PlanTrip(OmsiMapSummary summary, string installationRoot, IList<OmsiTripStopChoice> stops, OmsiPathNetwork network = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            network = network ?? OmsiPathNetwork.ForMap(summary, installationRoot);
            var timetable = OmsiTimetable.Read(RequireWorkspace());
            return OmsiTripPlanner.Plan(stops, timetable, network, (x, y) => summary.Tiles.FindIndex(tile => tile.X == x && tile.Y == y), !HasLeftHandTraffic());
        }

        /// <summary>
        /// Writes a planned trip: its trip file, the station links it lacks and the stops it adds. Everything is checked first and nothing is written
        /// when the plan has problems or the trip's file exists; returns the files written.
        /// </summary>
        public List<string> CreateTrip(OmsiTripPlan plan, string tripName, string destination, string line, string profileName, double runningMinutes)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (!plan.IsValid)
                throw new InvalidOperationException("The trip has problems: " + string.Join(" ", plan.Problems));
            var workspace = RequireWorkspace();
            var tripPath = TimetablePath(tripName, ".ttp");
            if (workspace.FileExists(tripPath))
                throw new InvalidOperationException("The map has a trip named " + tripName + " already.");
            var stopIds = new List<int> { plan.Legs[0].From.ObjectId };
            stopIds.AddRange(plan.Legs.Select(leg => leg.To.ObjectId));
            var files = new List<KeyValuePair<string, byte[]>>
            {
                new KeyValuePair<string, byte[]>(tripPath, OmsiTimetableWriter.CreateTrip(destination, line, stopIds, profileName, runningMinutes))
            };
            var known = OmsiTimetable.Read(workspace).BusStops.Select(stop => stop.ObjectId).ToList();
            var added = new List<OmsiTripStopChoice>();
            foreach (var stop in plan.Legs.Select(leg => leg.From).Concat(plan.Legs.Select(leg => leg.To)))
                if (!known.Contains(stop.ObjectId) && !added.Exists(each => each.ObjectId == stop.ObjectId))
                    added.Add(stop);
            if (added.Count > 0)
                files.Add(new KeyValuePair<string, byte[]>(BusStopsFile, OmsiTimetableWriter.AppendBusStops(
                    workspace.FileExists(BusStopsFile) ? workspace.ReadAllBytes(BusStopsFile) : null, added)));
            if (plan.Legs.Any(leg => !leg.Existing))
                files.Add(new KeyValuePair<string, byte[]>(StationLinksFile, OmsiTimetableWriter.AppendStationLinks(
                    workspace.FileExists(StationLinksFile) ? workspace.ReadAllBytes(StationLinksFile) : null, plan.Legs)));
            foreach (var file in files)
                workspace.Save(file.Key, file.Value);
            return files.Select(file => file.Key).ToList();
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
