using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The path numbers a tile gives its elements (ADR 097): OMSI counts every <c>[path]</c> of the tile's objects and splines (<c>[spline_h]</c> counts like
    /// <c>[spline]</c>) in the order of the tile file, and a track entry names the number of its path in its fourth field. Verified against all 817 entries of the
    /// tracks of Grundorf and Berlin-Spandau.
    /// </summary>
    public static class OmsiTilePathNumbers
    {
        /// <summary>The number of the first path of each element of a tile, by element ID.</summary>
        /// <param name="tile">The tile file, read without a layout or with the tile layouts.</param>
        /// <param name="pathCount">How many paths the object or spline file of an asset path has; null when it has none or is unknown.</param>
        public static Dictionary<int, int> Of(OmsiRecordFile tile, Func<string, int?> pathCount)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (pathCount == null)
                throw new ArgumentNullException(nameof(pathCount));
            var result = new Dictionary<int, int>();
            var number = 0;
            foreach (var record in tile.Records)
            {
                var name = record.Name;
                if (!string.Equals(name, "object", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "spline", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "spline_h", StringComparison.OrdinalIgnoreCase))
                    continue;
                var values = record.AllValues();
                if (values.Count < 3 || !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    continue;
                if (!result.ContainsKey(id))
                    result[id] = number;
                number += pathCount(values[1]) ?? 0;
            }
            return result;
        }
    }

    /// <summary>A station of a planned track: its place along the entries.</summary>
    public sealed class OmsiTrackStation
    {
        public OmsiTripStopChoice Stop { get; internal set; }

        /// <summary>The entry of the track the station lies on, from 0.</summary>
        public int Entry { get; internal set; }

        /// <summary>Metres from the start of that entry, on the path's own length.</summary>
        public double Position { get; internal set; }

        /// <summary>Metres from the track's start (the start of its first entry).</summary>
        public double Distance { get; internal set; }

        /// <summary>Metres between the station's object and the track, positive on the right in the way of travel.</summary>
        public double Lateral { get; internal set; }
    }

    /// <summary>The way a train or tram takes between stations over the rails of the map, as the entries of a track.</summary>
    public sealed class OmsiTrackPlan
    {
        public List<OmsiPathEntry> Entries { get; } = new List<OmsiPathEntry>();
        public List<OmsiTrackStation> Stations { get; } = new List<OmsiTrackStation>();

        /// <summary>The paths of the entries as lines on the map, in world metres.</summary>
        public List<OmsiMapPoint[]> Lines { get; } = new List<OmsiMapPoint[]>();

        public List<string> Problems { get; } = new List<string>();
        public double LengthMeters { get; internal set; }
        public bool IsValid => Problems.Count == 0 && Entries.Count > 0 && Stations.Count >= 2;
    }

    /// <summary>Finds the way over the rails (paths of kind 2) from station to station and the entries of the track it makes (ADR 097).</summary>
    public static class OmsiTrackPlanner
    {
        private const int MostPlaces = 4;
        private const double Unreachable = 1e7;
        private const double ReachMeters = 8.0;

        /// <param name="items">The stops in order (objects with their places) and, between them, waypoints that lead the way over another rail.</param>
        /// <param name="network">The rails of the map: <c>OmsiPathNetwork.Build(paths, 2)</c>.</param>
        /// <param name="tileIndexOf">The index of the tile in global.cfg for a tile's X and Y.</param>
        public static OmsiTrackPlan Plan(IList<OmsiTripStopChoice> items, OmsiPathNetwork network, Func<int, int, int> tileIndexOf)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (tileIndexOf == null)
                throw new ArgumentNullException(nameof(tileIndexOf));
            var plan = new OmsiTrackPlan();
            // A waypoint before the first station or after the last leads nowhere.
            var points = items.ToList();
            while (points.Count > 0 && points[0].IsWaypoint)
                points.RemoveAt(0);
            while (points.Count > 0 && points[points.Count - 1].IsWaypoint)
                points.RemoveAt(points.Count - 1);
            if (points.Count(item => !item.IsWaypoint) < 2)
            {
                plan.Problems.Add("A trip needs at least two stops.");
                return plan;
            }
            var candidates = new List<List<OmsiStopPlace>>();
            foreach (var point in points)
            {
                var found = network.PlaceWaypointCandidates(point.World, ReachMeters);
                if (found.Count > MostPlaces)
                    found = found.GetRange(0, MostPlaces);
                if (found.Count == 0)
                {
                    plan.Problems.Add((point.IsWaypoint ? "A waypoint" : "The stop " + point.Name) + " has no rail within " + ReachMeters.ToString("0", CultureInfo.InvariantCulture) + " m.");
                    return plan;
                }
                candidates.Add(found);
            }
            // The cheapest way over all the places the stations could be served from, the way the road planner chooses lanes.
            var count = points.Count;
            var best = new double[count][];
            var before = new int[count][];
            var routes = new OmsiNetworkRoute[count][][];
            for (var index = 0; index < count; index++)
            {
                best[index] = new double[candidates[index].Count];
                before[index] = new int[candidates[index].Count];
                routes[index] = new OmsiNetworkRoute[candidates[index].Count][];
            }
            for (var index = 1; index < count; index++)
                for (var place = 0; place < candidates[index].Count; place++)
                {
                    var cheapest = double.MaxValue;
                    routes[index][place] = new OmsiNetworkRoute[candidates[index - 1].Count];
                    for (var previous = 0; previous < candidates[index - 1].Count; previous++)
                    {
                        var route = network.Route(candidates[index - 1][previous], candidates[index][place]);
                        routes[index][place][previous] = route;
                        var cost = best[index - 1][previous] + (route == null ? Unreachable : route.LengthMeters + 2.0 * candidates[index][place].Distance);
                        if (cost < cheapest)
                        {
                            cheapest = cost;
                            before[index][place] = previous;
                        }
                    }
                    best[index][place] = cheapest;
                }
            var last = 0;
            for (var place = 1; place < candidates[count - 1].Count; place++)
                if (best[count - 1][place] < best[count - 1][last])
                    last = place;
            if (best[count - 1][last] >= Unreachable)
            {
                // Name the first leg no rail leads over.
                for (var index = 1; index < count; index++)
                {
                    var reachable = candidates[index - 1].Any(from => candidates[index].Any(to => network.Route(from, to) != null));
                    if (!reachable)
                    {
                        plan.Problems.Add("No way leads over the rails from " + Describe(points[index - 1]) + " to " + Describe(points[index]) + ".");
                        return plan;
                    }
                }
                plan.Problems.Add("No way leads over the rails from the first to the last stop.");
                return plan;
            }
            var chosen = new int[count];
            chosen[count - 1] = last;
            for (var index = count - 1; index > 0; index--)
                chosen[index - 1] = before[index][chosen[index]];

            // The entries: the lanes of each leg, a lane a leg shares with the one before counted once.
            var lanes = new List<OmsiLane>();
            var entryOfPoint = new int[count];
            lanes.Add(candidates[0][chosen[0]].Lane);
            entryOfPoint[0] = 0;
            for (var index = 1; index < count; index++)
            {
                var route = routes[index][chosen[index]][chosen[index - 1]];
                foreach (var lane in route.Lanes)
                    if (lanes.Count == 0 || !ReferenceEquals(lanes[lanes.Count - 1], lane))
                        lanes.Add(lane);
                entryOfPoint[index] = lanes.Count - 1;
            }
            foreach (var lane in lanes)
            {
                plan.Entries.Add(new OmsiPathEntry
                {
                    ElementId = lane.Path.ElementId, PathIndex = lane.Path.PathIndex, TileIndex = tileIndexOf(lane.Path.TileX, lane.Path.TileY), LengthMeters = lane.LengthMeters
                });
                plan.Lines.Add(lane.Points);
            }
            for (var index = 0; index < count; index++)
            {
                if (points[index].IsWaypoint)
                    continue;
                var place = candidates[index][chosen[index]];
                var entry = entryOfPoint[index];
                var distance = plan.Entries.Take(entry).Sum(each => each.LengthMeters) + place.Position;
                plan.Stations.Add(new OmsiTrackStation
                {
                    Stop = points[index], Entry = entry, Position = place.Position, Distance = distance, Lateral = place.Right ? place.Distance : -place.Distance
                });
            }
            plan.LengthMeters = plan.Stations[plan.Stations.Count - 1].Distance - plan.Stations[0].Distance;
            return plan;
        }

        private static string Describe(OmsiTripStopChoice point)
        {
            return point.IsWaypoint ? "a waypoint" : point.Name;
        }
    }

    /// <summary>The files of a track and of a trip that runs on it.</summary>
    public static class OmsiTrackWriter
    {
        private const string Header = "Created with OMSI MapWorks";

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        /// <summary>
        /// A track file: one <c>[track_entry]</c> per path with its element, its place in the element, its tile, the number OMSI keeps for the path in its tile
        /// (<paramref name="tileNumbers"/>, see <see cref="OmsiTilePathNumbers"/>) and its length. The last field is 0; openOMSI reads the first three and the length.
        /// </summary>
        public static byte[] CreateTrack(IList<OmsiPathEntry> entries, IList<int> tileNumbers)
        {
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("A track needs entries.", nameof(entries));
            if (tileNumbers == null || tileNumbers.Count != entries.Count)
                throw new ArgumentException("Every entry needs its number in its tile.", nameof(tileNumbers));
            var text = new StringBuilder();
            text.Append("-----------------------\r\nTime Table Track File\r\n-----------------------\r\n\r\n").Append(Header).Append("\r\n\r\n");
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                text.Append("   ").Append(Int(index)).Append(":\r\n[track_entry]\r\n").Append(Int(entry.ElementId)).Append("\r\n").Append(Int(entry.PathIndex)).Append("\r\n")
                    .Append(Int(entry.TileIndex)).Append("\r\n").Append(Int(tileNumbers[index] + entry.PathIndex)).Append("\r\n")
                    .Append(entry.LengthMeters.ToString("0.0000000000", CultureInfo.InvariantCulture)).Append("\r\n0\r\n\r\n");
            }
            return new UTF8Encoding(false).GetBytes(text.ToString());
        }

        /// <summary>
        /// A trip on a track: <c>[trip]</c> with the track's name, the destination and the line, a <c>[station]</c> for each station and one profile with the
        /// running time. A station names its object, its entry in the track (from 0), its name and tile, the distance to the track, the distance from the
        /// track's start and along its entry, and the length of the stop.
        /// </summary>
        public static byte[] CreateTrip(string track, string destination, string line, IEnumerable<OmsiTrackStation> stations, string profileName, double runningMinutes)
        {
            var text = new StringBuilder();
            text.Append("-----------------------\r\nTime Table Trip File\r\n-----------------------\r\n\r\n").Append(Header).Append("\r\n\r\n[trip]\r\n").Append(OneLine(track)).Append("\r\n")
                .Append(OneLine(destination)).Append("\r\n").Append(OneLine(line)).Append("\r\n\r\n.........................\r\n        Stations\r\n.........................\r\n\r\n");
            foreach (var station in stations)
            {
                text.Append("[station]\r\n").Append(Int(station.Stop.ObjectId)).Append("\r\n").Append(Int(station.Entry)).Append("\r\n").Append(OneLine(station.Stop.Name)).Append("\r\n")
                    .Append(Int(station.Stop.TileIndex)).Append("\r\n").Append(station.Lateral.ToString("0.000", CultureInfo.InvariantCulture)).Append("\r\n")
                    .Append(station.Distance.ToString("0.000", CultureInfo.InvariantCulture)).Append("\r\n").Append(station.Position.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append("\r\n10.000\r\n\r\n");
            }
            text.Append("\r\n.........................\r\n        Profiles\r\n.........................\r\n\r\n[profile]\r\n").Append(OneLine(profileName)).Append("\r\n")
                .Append(runningMinutes.ToString("0.000", CultureInfo.InvariantCulture)).Append("\r\n\r\n");
            return new UTF8Encoding(false).GetBytes(text.ToString());
        }
    }
}
