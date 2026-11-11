using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A stop a new trip serves: an object of a tile, with the name the timetable shows.</summary>
    public sealed class OmsiTripStopChoice
    {
        public string Name { get; set; }

        /// <summary>Position of the stop's tile in the [map] list of global.cfg, from 0.</summary>
        public int TileIndex { get; set; }

        public int ObjectId { get; set; }

        public OmsiMapPoint World { get; set; }

        /// <summary>True when the stop is not in <c>Busstops.cfg</c> yet and gets an entry with the trip.</summary>
        public bool IsNew { get; set; }

        /// <summary>True for a point the trip only passes, to lead it over another road than the shortest; it has no object and no stop.</summary>
        public bool IsWaypoint { get; set; }
    }

    /// <summary>The way between two stops of a trip: the station link that is already in the timetable, or the one found on the map.</summary>
    public sealed class OmsiTripLeg
    {
        public OmsiTripStopChoice From { get; internal set; }
        public OmsiTripStopChoice To { get; internal set; }

        /// <summary>True when <c>StnLinks.cfg</c> has this link; nothing is written for it.</summary>
        public bool Existing { get; internal set; }

        /// <summary>True when waypoints lead the leg over a road of their choice.</summary>
        public bool HasWaypoints { get; internal set; }

        /// <summary>True when <c>StnLinks.cfg</c> has a link for this pair that the leg's waypoints replace; other trips over the pair use the new one too.</summary>
        public bool ReplacesLink { get; internal set; }

        public double LengthMeters { get; internal set; }
        public double FromPosition { get; internal set; }
        public double ToPosition { get; internal set; }
        public double FromDistance { get; internal set; }
        public double ToDistance { get; internal set; }
        public List<OmsiPathEntry> Entries { get; } = new List<OmsiPathEntry>();

        /// <summary>The lanes of the way as lines on the map, in world metres; empty for an existing link.</summary>
        public List<OmsiMapPoint[]> Lines { get; } = new List<OmsiMapPoint[]>();
    }

    /// <summary>What a new trip needs: its legs, and what keeps it from being written.</summary>
    public sealed class OmsiTripPlan
    {
        public List<OmsiTripLeg> Legs { get; } = new List<OmsiTripLeg>();

        /// <summary>Reasons the trip cannot be made, such as two stops no lane leads between.</summary>
        public List<string> Problems { get; } = new List<string>();

        public bool IsValid => Problems.Count == 0 && Legs.Count > 0;

        public double LengthMeters => Legs.Sum(leg => leg.LengthMeters);
    }

    /// <summary>
    /// Plans a trip over a sequence of stops (ADR 093): a leg the timetable already has a station link for is used as it is, any other is found
    /// on the map's vehicle lanes by <see cref="OmsiPathNetwork"/>.
    /// </summary>
    public static class OmsiTripPlanner
    {
        /// <param name="items">The stops in the order the trip serves them, at least two, with waypoints between them where the way is to lead over a road of choice.</param>
        /// <param name="timetable">The timetable whose station links count as there already.</param>
        /// <param name="network">The vehicle lanes of the map.</param>
        /// <param name="tileIndexOf">The position in the [map] list of the tile at a position, or -1.</param>
        /// <param name="tileCoordsOf">The tile at a position of the [map] list as x and y, to draw the way of a link the timetable has; null leaves such legs without a line.</param>
        public static OmsiTripPlan Plan(IList<OmsiTripStopChoice> items, OmsiTimetable timetable, OmsiPathNetwork network, Func<int, int, int> tileIndexOf,
            bool rightHandTraffic = true, Func<int, int[]> tileCoordsOf = null)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (timetable == null)
                throw new ArgumentNullException(nameof(timetable));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (tileIndexOf == null)
                throw new ArgumentNullException(nameof(tileIndexOf));
            var plan = new OmsiTripPlan();
            var stops = new List<OmsiTripStopChoice>();
            var between = new List<List<OmsiTripStopChoice>>();
            var current = new List<OmsiTripStopChoice>();
            foreach (var item in items)
            {
                if (item.IsWaypoint)
                {
                    // A waypoint before the first stop or after the last leads nowhere.
                    if (stops.Count > 0)
                        current.Add(item);
                    continue;
                }
                if (stops.Count > 0)
                {
                    between.Add(current);
                    current = new List<OmsiTripStopChoice>();
                }
                stops.Add(item);
            }
            if (stops.Count < 2)
            {
                plan.Problems.Add("A trip needs at least two stops.");
                return plan;
            }
            // Every stop could be served from several lanes of its road; the lane a bus stands on is the one it leaves on, so the lanes are chosen for
            // the whole trip at once: the cheapest way over all choices (as OMSI's own links join).
            const int MostLanes = 4;
            const double Unreachable = 1e7;
            var candidates = new List<List<OmsiStopPlace>>();
            foreach (var stop in stops)
            {
                var found = network.PlaceStopCandidates(stop.World, rightHandTraffic);
                candidates.Add(found.Count > MostLanes ? found.GetRange(0, MostLanes) : found);
            }
            var legCount = stops.Count - 1;
            var links = new OmsiStationLink[legCount];
            for (var index = 0; index < legCount; index++)
            {
                var link = timetable.StationLinks.Find(each => each.FromStopId == stops[index].ObjectId && each.ToStopId == stops[index + 1].ObjectId);
                links[index] = link != null && between[index].Count == 0 ? link : null;
            }
            // best[i][c]: the cheapest total length of the legs before stop i when it is served from its candidate c; before[i][c]: the candidate of the stop before.
            var best = new double[stops.Count][];
            var before = new int[stops.Count][];
            for (var index = 0; index < stops.Count; index++)
            {
                best[index] = new double[Math.Max(1, candidates[index].Count)];
                before[index] = new int[best[index].Length];
            }
            for (var index = 0; index < legCount; index++)
            {
                var fromCount = best[index].Length;
                var toCount = best[index + 1].Length;
                for (var to = 0; to < toCount; to++)
                {
                    var cheapest = double.MaxValue;
                    var chosen = 0;
                    for (var from = 0; from < fromCount; from++)
                    {
                        var cost = links[index] != null || candidates[index].Count == 0 || candidates[index + 1].Count == 0
                            ? 0.0
                            : LegCost(network, candidates[index][from], candidates[index + 1][to], between[index], Unreachable);
                        if (best[index][from] + cost < cheapest)
                        {
                            cheapest = best[index][from] + cost;
                            chosen = from;
                        }
                    }
                    best[index + 1][to] = cheapest;
                    before[index + 1][to] = chosen;
                }
            }
            var choice = new int[stops.Count];
            var last = 0;
            for (var to = 1; to < best[stops.Count - 1].Length; to++)
                if (best[stops.Count - 1][to] < best[stops.Count - 1][last])
                    last = to;
            choice[stops.Count - 1] = last;
            for (var index = stops.Count - 1; index > 0; index--)
                choice[index - 1] = before[index][choice[index]];

            for (var index = 0; index < legCount; index++)
            {
                var from = stops[index];
                var to = stops[index + 1];
                var waypoints = between[index];
                var leg = new OmsiTripLeg { From = from, To = to, HasWaypoints = waypoints.Count > 0 };
                var existing = timetable.StationLinks.Find(each => each.FromStopId == from.ObjectId && each.ToStopId == to.ObjectId);
                leg.ReplacesLink = existing != null && waypoints.Count > 0;
                if (links[index] != null)
                {
                    leg.Existing = true;
                    leg.LengthMeters = links[index].LengthMeters;
                    DrawLink(leg, links[index], network, tileCoordsOf);
                    plan.Legs.Add(leg);
                    continue;
                }
                if (candidates[index].Count == 0 || candidates[index + 1].Count == 0)
                {
                    plan.Problems.Add((candidates[index].Count == 0 ? from.Name : to.Name) + " has no vehicle lane beside it.");
                    plan.Legs.Add(leg);
                    continue;
                }
                var result = RouteLeg(network, candidates[index][choice[index]], candidates[index + 1][choice[index + 1]], waypoints, out var problem);
                if (result == null)
                {
                    plan.Problems.Add(problem == null ? "No way leads from " + from.Name + " to " + to.Name + (waypoints.Count > 0 ? " over its waypoints." : ".")
                        : problem.Replace("{from}", from.Name).Replace("{to}", to.Name));
                    plan.Legs.Add(leg);
                    continue;
                }
                leg.LengthMeters = result.LengthMeters;
                leg.FromPosition = candidates[index][choice[index]].Position;
                leg.ToPosition = candidates[index + 1][choice[index + 1]].Position;
                leg.FromDistance = candidates[index][choice[index]].Distance;
                leg.ToDistance = candidates[index + 1][choice[index + 1]].Distance;
                foreach (var lane in result.Lanes)
                {
                    var tile = tileIndexOf(lane.Path.TileX, lane.Path.TileY);
                    if (tile < 0)
                    {
                        plan.Problems.Add("A lane between " + from.Name + " and " + to.Name + " lies on a tile the map does not list.");
                        break;
                    }
                    leg.Entries.Add(new OmsiPathEntry { ElementId = lane.Path.ElementId, PathIndex = lane.Path.PathIndex, TileIndex = tile, LengthMeters = lane.LengthMeters });
                    leg.Lines.Add(lane.Geometry);
                }
                plan.Legs.Add(leg);
            }
            return plan;
        }

        /// <summary>The length of the way from one place of a stop to one of the next over the waypoints, counting a little for the lanes' distance to the stops; a large number when there is none.</summary>
        private static double LegCost(OmsiPathNetwork network, OmsiStopPlace from, OmsiStopPlace to, List<OmsiTripStopChoice> waypoints, double unreachable)
        {
            var route = RouteLeg(network, from, to, waypoints, out _);
            return route == null ? unreachable : route.LengthMeters + 2.0 * (from.Distance + to.Distance);
        }

        /// <summary>The way from a place to a place over the waypoints, joined to one route whose length is the sum of the lane lengths; null when none leads there.</summary>
        private static OmsiNetworkRoute RouteLeg(OmsiPathNetwork network, OmsiStopPlace from, OmsiStopPlace to, List<OmsiTripStopChoice> waypoints, out string problem)
        {
            problem = null;
            var targets = new List<List<OmsiStopPlace>>();
            foreach (var waypoint in waypoints)
            {
                var found = network.PlaceWaypointCandidates(waypoint.World);
                if (found.Count == 0)
                {
                    problem = "A waypoint between {from} and {to} is not near a vehicle lane.";
                    return null;
                }
                targets.Add(found);
            }
            targets.Add(new List<OmsiStopPlace> { to });
            var joined = new OmsiNetworkRoute();
            var segmentFrom = new List<OmsiStopPlace> { from };
            var total = 0.0;
            foreach (var target in targets)
            {
                var route = network.Route(segmentFrom, target);
                if (route == null || route.Lanes.Count == 0)
                    return null;
                foreach (var lane in route.Lanes)
                    if (joined.Lanes.Count == 0 || joined.Lanes[joined.Lanes.Count - 1] != lane)
                        joined.Lanes.Add(lane);
                total += route.LengthMeters;
                var arrival = target.Find(place => place.Lane == route.Lanes[route.Lanes.Count - 1]) ?? target[0];
                segmentFrom = new List<OmsiStopPlace> { arrival };
            }
            joined.LengthMeters = total;
            return joined;
        }

        /// <summary>The lines of a link the timetable has, from the lanes of its entries, and the place its last stop has on its last lane; null when the lanes are not found.</summary>
        private static OmsiStopPlace DrawLink(OmsiTripLeg leg, OmsiStationLink link, OmsiPathNetwork network, Func<int, int[]> tileCoordsOf)
        {
            if (tileCoordsOf == null)
                return null;
            OmsiLane before = null;
            foreach (var entry in link.Entries)
            {
                var tile = tileCoordsOf(entry.TileIndex);
                if (tile == null)
                    return null;
                var candidates = network.LanesOfPath(tile[0], tile[1], entry.ElementId, entry.PathIndex);
                if (candidates.Count == 0)
                    continue;
                var lane = candidates.FirstOrDefault(each => before != null && before.Next.Contains(each.Index)) ?? candidates[0];
                leg.Lines.Add(lane.Geometry);
                before = lane;
            }
            return before == null ? null : new OmsiStopPlace { Lane = before, Position = link.ToPosition, Distance = link.ToDistance };
        }
    }

    /// <summary>The text of new timetable files and of what is added to existing ones, in the form OMSI's own editor writes (ADR 093).</summary>
    public static class OmsiTimetableWriter
    {
        private const string Header = "Created with OMSI MapWorks";

        /// <summary><c>Busstops.cfg</c> with the new stops added behind the others.</summary>
        public static byte[] AppendBusStops(byte[] existing, IEnumerable<OmsiTripStopChoice> stops)
        {
            var text = new StringBuilder();
            foreach (var stop in stops)
                text.Append("[busstop]\r\n").Append(OneLine(stop.Name)).Append("\r\n").Append(Int(stop.TileIndex)).Append("\r\n").Append(Int(stop.ObjectId))
                    .Append("\r\n").Append(Number(0.0)).Append("\r\n0\r\n0\r\n\r\n\r\n");
            return Append(existing, text.ToString(), "---------------------------\r\nTime Table BusStopList File\r\n---------------------------\r\n\r\n" + Header + "\r\n\r\n");
        }

        /// <summary>
        /// <c>Busstops.cfg</c> with the name of a stop set: the stop of the object is renamed where it is listed, else added behind the others.
        /// </summary>
        public static byte[] SetBusStopName(byte[] existing, int tileIndex, int objectId, string name)
        {
            if (existing != null)
            {
                var file = OmsiRecordFile.Parse(existing, null);
                foreach (var record in file.Records)
                {
                    if (!string.Equals(record.Name, "busstop", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var values = record.AllValues();
                    if (values.Count < 3 || !int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tile) ||
                        !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || tile != tileIndex || id != objectId)
                        continue;
                    if (record.Fields.Count > 0)
                        record.Fields[0].Text = OneLine(name);
                    else if (record.Trailer.Count > 0)
                        record.Trailer[0].Text = OneLine(name);
                    return file.ToBytes();
                }
            }
            return AppendBusStops(existing, new[] { new OmsiTripStopChoice { Name = name, TileIndex = tileIndex, ObjectId = objectId } });
        }

        /// <summary>
        /// <c>StnLinks.cfg</c> with the legs the timetable lacks added behind the others; a leg whose waypoints replace a link takes that link's place
        /// (the old one is cut out, the new one follows the others).
        /// </summary>
        public static byte[] AppendStationLinks(byte[] existing, IEnumerable<OmsiTripLeg> legs)
        {
            var all = legs.ToList();
            if (existing != null && all.Any(leg => leg.ReplacesLink))
                existing = RemoveStationLinks(existing, all.Where(leg => leg.ReplacesLink).Select(leg => new KeyValuePair<int, int>(leg.From.ObjectId, leg.To.ObjectId)).ToList());
            var text = new StringBuilder();
            foreach (var leg in all)
            {
                if (leg.Existing)
                    continue;
                text.Append(OneLine(leg.From.Name)).Append(" ==> ").Append(OneLine(leg.To.Name)).Append("\r\n\r\n[StnLink]\r\n").Append(Number(leg.LengthMeters)).Append("\r\n")
                    .Append(Int(leg.From.ObjectId)).Append("\r\n").Append(Int(leg.To.ObjectId)).Append("\r\n").Append(Number(leg.FromDistance)).Append("\r\n")
                    .Append(Number(leg.ToDistance)).Append("\r\n").Append(Number(leg.FromPosition)).Append("\r\n").Append(Number(leg.ToPosition)).Append("\r\n0\r\n")
                    .Append(Int(Math.Max(0, leg.Entries.Count - 1))).Append("\r\n\r\n");
                for (var index = 0; index < leg.Entries.Count; index++)
                {
                    var entry = leg.Entries[index];
                    text.Append("   ").Append(Int(index)).Append(":\r\n[StnLink_entry]\r\n").Append(Int(entry.ElementId)).Append("\r\n").Append(Int(entry.PathIndex)).Append("\r\n")
                        .Append(Int(entry.TileIndex)).Append("\r\n").Append(Number(entry.LengthMeters)).Append("\r\n-1\r\n0\r\n0\r\n\r\n");
                }
            }
            return Append(existing, text.ToString(), "---------------------------\r\nTime Table StnLinkList File\r\n---------------------------\r\n\r\n" + Header + "\r\n\r\n");
        }

        /// <summary>Cuts the links of some stop pairs out of <c>StnLinks.cfg</c>: from the line that names them ("A ==> B") to the one that names the next.</summary>
        private static byte[] RemoveStationLinks(byte[] bytes, IList<KeyValuePair<int, int>> pairs)
        {
            var text = OmsiTextFile.Decode(bytes, out var encoding);
            var lines = new List<string>(text.Split('\n'));
            for (var index = 0; index < lines.Count; index++)
            {
                if (!string.Equals(lines[index].Trim(), "[StnLink]", StringComparison.OrdinalIgnoreCase) || index + 3 >= lines.Count)
                    continue;
                if (!int.TryParse(lines[index + 2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) ||
                    !int.TryParse(lines[index + 3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var to) || !pairs.Contains(new KeyValuePair<int, int>(from, to)))
                    continue;
                var start = index >= 2 && lines[index - 2].Contains("==>") && lines[index - 1].Trim().Length == 0 ? index - 2 : index;
                var end = lines.Count;
                for (var next = index + 1; next < lines.Count; next++)
                    if (string.Equals(lines[next].Trim(), "[StnLink]", StringComparison.OrdinalIgnoreCase))
                    {
                        end = next >= 2 && lines[next - 2].Contains("==>") && lines[next - 1].Trim().Length == 0 ? next - 2 : next;
                        break;
                    }
                lines.RemoveRange(start, end - start);
                index = start - 1;
            }
            return OmsiTextFile.Encode(string.Join("\n", lines), encoding);
        }

        /// <summary>A trip file: where it goes, the line it shows, its stops in order and one profile with the running time in minutes.</summary>
        public static byte[] CreateTrip(string destination, string line, IEnumerable<int> stopIds, string profileName, double runningMinutes)
        {
            var text = new StringBuilder();
            text.Append("-----------------------\r\nTime Table Trip File\r\n-----------------------\r\n\r\n").Append(Header).Append("\r\n\r\n[trip]\r\n\r\n")
                .Append(OneLine(destination)).Append("\r\n").Append(OneLine(line)).Append("\r\n\r\n.........................\r\n        Stations\r\n.........................\r\n\r\n");
            foreach (var id in stopIds)
                text.Append("[station_typ2]\r\n").Append(Int(id)).Append("\r\n\r\n");
            text.Append("\r\n.........................\r\n        Profiles\r\n.........................\r\n\r\n[profile]\r\n").Append(OneLine(profileName)).Append("\r\n")
                .Append(runningMinutes.ToString("0.000", CultureInfo.InvariantCulture)).Append("\r\n");
            return Encoding.UTF8.GetBytes(text.ToString());
        }

        /// <summary>A line file with one tour that runs on all days and has no trips yet; the player may drive it.</summary>
        public static byte[] CreateLine(string vehicleGroup)
        {
            var text = "-----------------------\r\nTime Table Line File\r\n-----------------------\r\n\r\n" + Header + "\r\n\r\n[userallowed]\r\n\r\n[priority]\r\n1\r\n" +
                       "------------------------------------\r\n\r\n[newtour]\r\n1\r\n" + OneLine(vehicleGroup) + "\r\n1023\r\n\r\n";
            return Encoding.UTF8.GetBytes(text);
        }

        private static byte[] Append(byte[] existing, string addition, string newHeader)
        {
            if (existing == null)
                return Encoding.UTF8.GetBytes(newHeader + addition);
            var text = OmsiTextFile.Decode(existing, out var encoding);
            if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal))
                text += "\r\n";
            return OmsiTextFile.Encode(text + addition, encoding);
        }

        /// <summary>A number the way OMSI writes it: ten decimals, but only three significant digits (51.4, 272, 2.28), which also rounds off the float noise of a length.</summary>
        private static string Number(double value)
        {
            if (value != 0.0 && !double.IsInfinity(value) && !double.IsNaN(value))
            {
                var scale = Math.Pow(10.0, 2 - (int)Math.Floor(Math.Log10(Math.Abs(value))));
                value = Math.Round(value * scale) / scale;
            }
            return value.ToString("0.0000000000", CultureInfo.InvariantCulture);
        }

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
