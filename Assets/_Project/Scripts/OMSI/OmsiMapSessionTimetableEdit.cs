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

        /// <summary>
        /// Takes a line away with the trips only its tours run and the tracks only those trips drive (ADR 096, addendum of 15 November 2026), in one
        /// undoable step. A trip another line runs, also a line of a chrono variant, stays, and so does a track another trip drives. Refused while a
        /// chrono variant switches the line off.
        /// </summary>
        public OmsiTerrainChange DeleteLine(string line)
        {
            var workspace = RequireWorkspace();
            var variants = OmsiChrono.ReadVariants(workspace);
            var users = variants.Where(variant => variant.DeactivatedLines.Contains(line, StringComparer.OrdinalIgnoreCase)).Select(variant => variant.Title).ToList();
            if (users.Count > 0)
                throw new InvalidOperationException("The chrono variants " + string.Join(", ", users) + " switch the line " + line + " off.");
            var linePath = TimetablePath(line, ".ttl");
            var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(linePath, null) };
            if (!workspace.FileExists(linePath))
                return TimetableFilesChange(files);

            var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tour in ReadLineFile(line).Tours)
                foreach (var trip in tour.Trips)
                    if (!string.IsNullOrWhiteSpace(trip.Trip))
                        own.Add(trip.Trip.Trim());

            // Trips the other lines run, here and in the chrono variants, stay.
            var otherLines = workspace.EnumerateFiles(TimetableFolder, "*.ttl", false).Where(path => !SamePath(path, linePath)).ToList();
            foreach (var variant in variants)
                otherLines.AddRange(workspace.EnumerateFiles(Path.Combine(variant.Folder, TimetableFolder), "*.ttl", false));
            foreach (var path in otherLines)
            {
                if (own.Count == 0)
                    break;
                foreach (var tour in OmsiLineFile.Parse(workspace.ReadAllBytes(path)).Tours)
                    foreach (var trip in tour.Trips)
                        own.Remove((trip.Trip ?? string.Empty).Trim());
            }
            if (own.Count == 0)
                return TimetableFilesChange(files);

            // The trips by their file names, so a tour naming a trip in other letter case still finds its file.
            var trips = OmsiTimetable.ReadTrips(workspace, TimetableFolder);
            var gone = new HashSet<string>(own.Where(trips.ContainsKey).Select(trip => trips[trip].Name), StringComparer.OrdinalIgnoreCase);
            foreach (var trip in gone.OrderBy(trip => trip, StringComparer.OrdinalIgnoreCase))
                files.Add(new KeyValuePair<string, byte[]>(TimetablePath(trip, ".ttp"), null));

            // A track goes when only the trips that go drive it: no other trip, with or without a tour, here or in a chrono variant.
            var tracks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var trip in gone)
                if (!string.IsNullOrWhiteSpace(trips[trip].Track))
                    tracks.Add(trips[trip].Track.Trim());
            foreach (var trip in trips.Values)
                if (!gone.Contains(trip.Name) && !string.IsNullOrWhiteSpace(trip.Track))
                    tracks.Remove(trip.Track.Trim());
            foreach (var variant in variants)
            {
                if (tracks.Count == 0)
                    break;
                foreach (var trip in OmsiTimetable.ReadTrips(workspace, Path.Combine(variant.Folder, TimetableFolder)).Values)
                    if (!string.IsNullOrWhiteSpace(trip.Track))
                        tracks.Remove(trip.Track.Trim());
            }
            if (tracks.Count > 0)
            {
                // The track files by their names on disk.
                var trackFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in workspace.EnumerateFiles(TimetableFolder, "*.ttr", false))
                    if (string.Equals(Path.GetExtension(path), ".ttr", StringComparison.OrdinalIgnoreCase))
                        trackFiles[Path.GetFileNameWithoutExtension(path)] = Path.GetFileNameWithoutExtension(path);
                foreach (var track in tracks.Where(trackFiles.ContainsKey).Select(track => trackFiles[track]).OrderBy(track => track, StringComparer.OrdinalIgnoreCase))
                    files.Add(new KeyValuePair<string, byte[]>(TimetablePath(track, ".ttr"), null));
            }
            return TimetableFilesChange(files);
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
                throw new OmsiTripPlanException(trip, plan?.Problems);
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
        /// The timetable files a deletion takes along (ADR 096, addendum): the objects' own entries in Busstops.cfg and the station links from and to
        /// them go in the same step, and those references are returned as resolved. References elsewhere, such as a trip or a track, still block, so
        /// the refusal names only them. Null while what is left of the two files would still name an object, such as a link between two other stops
        /// that runs over it; then only the own entries that would really go count as resolved, so the refusal names what is left and nothing is written.
        /// </summary>
        private List<OmsiObjectChange.FileState> UnusedStopEntries(OmsiMapSummary summary, OmsiTileSummary tile, ICollection<int> ids, OmsiReferenceIndex references,
            out List<OmsiReference> resolved)
        {
            resolved = null;
            var own = new List<OmsiReference>();
            foreach (var id in ids)
                foreach (var reference in references.To(id))
                    if ((reference.Kind == "bus stop" && SamePath(reference.File, BusStopsFile)) ||
                        ((reference.Kind == "station link" || reference.Kind == "station link path") && SamePath(reference.File, StationLinksFile)))
                        own.Add(reference);
            if (own.Count == 0)
                return null;

            var workspace = RequireWorkspace();
            var tileIndex = summary.Tiles.IndexOf(tile);
            var files = new List<OmsiObjectChange.FileState>();
            var remaining = new List<OmsiReference>();
            foreach (var path in new[] { BusStopsFile, StationLinksFile })
            {
                if (!workspace.FileExists(path))
                    continue;
                var before = workspace.ReadAllBytes(path);
                var after = before;
                if (path == BusStopsFile)
                {
                    foreach (var id in ids)
                        after = OmsiTimetableWriter.RemoveBusStop(after, tileIndex, id);
                }
                else
                {
                    var pairs = OmsiTimetable.Read(workspace).StationLinks.Where(link => ids.Contains(link.FromStopId) || ids.Contains(link.ToStopId))
                        .Select(link => new KeyValuePair<int, int>(link.FromStopId, link.ToStopId)).Distinct().ToList();
                    if (pairs.Count > 0)
                        after = OmsiTimetableWriter.RemoveStationLinks(after, pairs);
                }
                // What is left must not name the objects any more; else the file keeps them and they stay.
                remaining.AddRange(OmsiReferenceIndex.TimetableReferences(path, after).Where(reference => ids.Contains(reference.TargetId)));
                if (!SameBytes(before, after))
                    files.Add(new OmsiObjectChange.FileState { RelativePath = path, Before = before, After = after });
            }
            if (remaining.Count == 0)
            {
                resolved = own;
                return files;
            }

            // Nothing is written, but the entries that would really go do not block: the refusal names only what is left.
            resolved = new List<OmsiReference>(own);
            foreach (var left in remaining)
            {
                var match = resolved.FindIndex(reference => reference.TargetId == left.TargetId && reference.Kind == left.Kind && SamePath(reference.File, left.File));
                if (match >= 0)
                    resolved.RemoveAt(match);
            }
            return null;
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
