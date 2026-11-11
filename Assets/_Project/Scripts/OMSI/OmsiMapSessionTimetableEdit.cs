using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Deleting and renaming what a timetable is made of (ADR 096): lines (<c>.ttl</c>), trips (<c>.ttp</c>) and bus stops (<c>Busstops.cfg</c> with the station links of the stop).
    /// Every change keeps the references right in one undoable step; what something else still names is refused, with the names.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        private static string TimetableName(string name, string what)
        {
            var value = (name ?? string.Empty).Trim();
            if (value.Length == 0 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value == "." || value == "..")
                throw new ArgumentException(what + " needs a name that is a valid file name.");
            return value;
        }

        /// <summary>Files after a change (<c>null</c> content takes a file away) as one undoable step; null when nothing changes.</summary>
        private OmsiTerrainChange TimetableFilesChange(IEnumerable<KeyValuePair<string, byte[]>> files)
        {
            var workspace = RequireWorkspace();
            var change = new OmsiTerrainChange { Painted = true, TimetableFiles = true };
            foreach (var file in files)
            {
                var before = workspace.FileExists(file.Key) ? workspace.ReadAllBytes(file.Key) : null;
                if (before == null && file.Value == null)
                    continue;
                if (before != null && file.Value != null && SameBytes(before, file.Value))
                    continue;
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = file.Key, Before = before, After = file.Value });
            }
            if (change.Files.Count == 0)
                return null;
            ApplyFiles(change.Files, false);
            return change;
        }

        /// <summary>The tours that run a trip, as "line tour".</summary>
        public List<string> TripUsers(string trip)
        {
            var result = new List<string>();
            foreach (var line in TimetableLineNames())
                foreach (var tour in ReadLineFile(line).Tours)
                    if (tour.Trips.Any(each => string.Equals(each.Trip, trip, StringComparison.OrdinalIgnoreCase)))
                        result.Add(line + " " + tour.Number);
            return result;
        }

        /// <summary>The trips that stop at an object.</summary>
        public List<string> StopUsers(int objectId)
        {
            return OmsiTimetable.Read(RequireWorkspace()).Trips.Values.Where(trip => trip.Stops.Any(stop => stop.ObjectId == objectId)).Select(trip => trip.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The chrono variants that switch a line off.</summary>
        private List<string> ChronoUsers(string line)
        {
            return OmsiChrono.ReadVariants(RequireWorkspace()).Where(variant => variant.DeactivatedLines.Contains(line, StringComparer.OrdinalIgnoreCase))
                .Select(variant => variant.Title).ToList();
        }

        /// <summary>Takes a trip away; refused while a tour runs it.</summary>
        public OmsiTerrainChange DeleteTrip(string trip)
        {
            var users = TripUsers(trip);
            if (users.Count > 0)
                throw new InvalidOperationException("The tours " + string.Join(", ", users) + " run the trip " + trip + ".");
            return TimetableFilesChange(new[] { new KeyValuePair<string, byte[]>(TimetablePath(trip, ".ttp"), null) });
        }

        /// <summary>Renames a trip file and the tours that run it.</summary>
        public OmsiTerrainChange RenameTrip(string trip, string name)
        {
            name = TimetableName(name, "A trip");
            var workspace = RequireWorkspace();
            var oldPath = TimetablePath(trip, ".ttp");
            if (!workspace.FileExists(oldPath))
                throw new InvalidOperationException("The map has no trip " + trip + ".");
            if (string.Equals(trip, name, StringComparison.Ordinal))
                return null;
            var newPath = TimetablePath(name, ".ttp");
            if (workspace.FileExists(newPath) && !string.Equals(trip, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The map has a trip named " + name + " already.");
            var files = new List<KeyValuePair<string, byte[]>>();
            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(new KeyValuePair<string, byte[]>(newPath, workspace.ReadAllBytes(oldPath)));
                files.Add(new KeyValuePair<string, byte[]>(oldPath, null));
            }
            else
            {
                files.Add(new KeyValuePair<string, byte[]>(oldPath, null));
                files.Add(new KeyValuePair<string, byte[]>(newPath, workspace.ReadAllBytes(oldPath)));
            }
            foreach (var line in TimetableLineNames())
            {
                var path = TimetablePath(line, ".ttl");
                var file = OmsiLineFile.Parse(workspace.ReadAllBytes(path));
                var tours = file.Tours;
                var changed = false;
                for (var tour = 0; tour < tours.Count; tour++)
                    for (var index = 0; index < tours[tour].Trips.Count; index++)
                        if (string.Equals(tours[tour].Trips[index].Trip, trip, StringComparison.OrdinalIgnoreCase))
                        {
                            file.SetTripName(tour, index, name);
                            changed = true;
                        }
                if (changed)
                    files.Add(new KeyValuePair<string, byte[]>(path, file.ToBytes()));
            }
            return TimetableFilesChange(files);
        }

        /// <summary>Takes a line away (its trips stay); refused while a chrono variant switches it off.</summary>
        public OmsiTerrainChange DeleteLine(string line)
        {
            var users = ChronoUsers(line);
            if (users.Count > 0)
                throw new InvalidOperationException("The chrono variants " + string.Join(", ", users) + " switch the line " + line + " off.");
            return TimetableFilesChange(new[] { new KeyValuePair<string, byte[]>(TimetablePath(line, ".ttl"), null) });
        }

        /// <summary>Renames a line file; refused while a chrono variant names it.</summary>
        public OmsiTerrainChange RenameLine(string line, string name)
        {
            name = TimetableName(name, "A line");
            var workspace = RequireWorkspace();
            var oldPath = TimetablePath(line, ".ttl");
            if (!workspace.FileExists(oldPath))
                throw new InvalidOperationException("The map has no line " + line + ".");
            if (string.Equals(line, name, StringComparison.Ordinal))
                return null;
            var users = ChronoUsers(line);
            if (users.Count > 0)
                throw new InvalidOperationException("The chrono variants " + string.Join(", ", users) + " switch the line " + line + " off.");
            var newPath = TimetablePath(name, ".ttl");
            if (workspace.FileExists(newPath) && !string.Equals(line, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The map has a line named " + name + " already.");
            var bytes = workspace.ReadAllBytes(oldPath);
            return TimetableFilesChange(new[]
            {
                new KeyValuePair<string, byte[]>(oldPath, null),
                new KeyValuePair<string, byte[]>(newPath, bytes)
            });
        }

        /// <summary>
        /// Puts the stops of a plan into an existing trip: its stop list is replaced (what the profiles say of single stops follows the stops that stay), the stops
        /// and station links the timetable lacks are added, and the destination is set when given. Times, tours and the line stay. One undoable step.
        /// </summary>
        public OmsiTerrainChange ReplaceTripStops(string trip, OmsiTripPlan plan, string destination)
        {
            if (plan == null || !plan.IsValid)
                throw new InvalidOperationException("The trip has problems: " + string.Join(" ", plan?.Problems ?? new List<string>()));
            var workspace = RequireWorkspace();
            var path = TimetablePath(trip, ".ttp");
            if (!workspace.FileExists(path))
                throw new InvalidOperationException("The map has no trip " + trip + ".");
            var file = OmsiTripFile.Parse(workspace.ReadAllBytes(path));
            var ids = new List<int> { plan.Legs[0].From.ObjectId };
            ids.AddRange(plan.Legs.Select(leg => leg.To.ObjectId));
            file.SetStops(ids);
            if (!string.IsNullOrWhiteSpace(destination))
                file.SetDestination(destination);
            var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(path, file.ToBytes()) };
            var known = OmsiTimetable.Read(workspace).BusStops.Select(stop => stop.ObjectId).ToList();
            var stops = new List<OmsiTripStopChoice>();
            foreach (var leg in plan.Legs)
                foreach (var stop in new[] { leg.From, leg.To })
                    if (!known.Contains(stop.ObjectId) && !stops.Exists(each => each.ObjectId == stop.ObjectId))
                        stops.Add(stop);
            if (stops.Count > 0)
                files.Add(new KeyValuePair<string, byte[]>(BusStopsFile, OmsiTimetableWriter.AppendBusStops(workspace.FileExists(BusStopsFile) ? workspace.ReadAllBytes(BusStopsFile) : null, stops)));
            if (plan.Legs.Any(leg => !leg.Existing))
                files.Add(new KeyValuePair<string, byte[]>(StationLinksFile, OmsiTimetableWriter.AppendStationLinks(
                    workspace.FileExists(StationLinksFile) ? workspace.ReadAllBytes(StationLinksFile) : null, plan.Legs)));
            return TimetableFilesChange(files);
        }

        /// <summary>
        /// Takes a bus stop out of the timetable: its entry in Busstops.cfg and the station links from and to it. The object stays on the map.
        /// Refused while a trip stops there.
        /// </summary>
        public OmsiTerrainChange RemoveBusStop(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord record)
        {
            var index = summary == null || tile == null ? -1 : summary.Tiles.IndexOf(tile);
            if (index < 0 || record == null)
                throw new ArgumentException("The object is not on a tile of the map.");
            var users = StopUsers(record.Id);
            if (users.Count > 0)
                throw new InvalidOperationException("The trips " + string.Join(", ", users) + " stop at object " + record.Id + ".");
            var workspace = RequireWorkspace();
            var files = new List<KeyValuePair<string, byte[]>>();
            if (workspace.FileExists(BusStopsFile))
                files.Add(new KeyValuePair<string, byte[]>(BusStopsFile, OmsiTimetableWriter.RemoveBusStop(workspace.ReadAllBytes(BusStopsFile), index, record.Id)));
            if (workspace.FileExists(StationLinksFile))
            {
                var pairs = OmsiTimetable.Read(workspace).StationLinks.Where(link => link.FromStopId == record.Id || link.ToStopId == record.Id)
                    .Select(link => new KeyValuePair<int, int>(link.FromStopId, link.ToStopId)).ToList();
                if (pairs.Count > 0)
                    files.Add(new KeyValuePair<string, byte[]>(StationLinksFile, OmsiTimetableWriter.RemoveStationLinks(workspace.ReadAllBytes(StationLinksFile), pairs)));
            }
            return TimetableFilesChange(files);
        }
    }
}
