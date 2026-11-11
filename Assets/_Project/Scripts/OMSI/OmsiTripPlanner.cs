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
    }

    /// <summary>The way between two stops of a trip: the station link that is already in the timetable, or the one found on the map.</summary>
    public sealed class OmsiTripLeg
    {
        public OmsiTripStopChoice From { get; internal set; }
        public OmsiTripStopChoice To { get; internal set; }

        /// <summary>True when <c>StnLinks.cfg</c> has this link; nothing is written for it.</summary>
        public bool Existing { get; internal set; }

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
        /// <param name="stops">The stops in the order the trip serves them, at least two.</param>
        /// <param name="timetable">The timetable whose station links count as there already.</param>
        /// <param name="network">The vehicle lanes of the map.</param>
        /// <param name="tileIndexOf">The position in the [map] list of the tile at a position, or -1.</param>
        public static OmsiTripPlan Plan(IList<OmsiTripStopChoice> stops, OmsiTimetable timetable, OmsiPathNetwork network, Func<int, int, int> tileIndexOf,
            bool rightHandTraffic = true)
        {
            if (stops == null)
                throw new ArgumentNullException(nameof(stops));
            if (timetable == null)
                throw new ArgumentNullException(nameof(timetable));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (tileIndexOf == null)
                throw new ArgumentNullException(nameof(tileIndexOf));
            var plan = new OmsiTripPlan();
            if (stops.Count < 2)
            {
                plan.Problems.Add("A trip needs at least two stops.");
                return plan;
            }
            var places = new Dictionary<int, List<OmsiStopPlace>>();
            Func<OmsiTripStopChoice, List<OmsiStopPlace>> placeOf = stop =>
            {
                if (!places.TryGetValue(stop.ObjectId, out var found))
                    places[stop.ObjectId] = found = network.PlaceStopCandidates(stop.World, rightHandTraffic);
                return found;
            };
            for (var index = 0; index + 1 < stops.Count; index++)
            {
                var from = stops[index];
                var to = stops[index + 1];
                var leg = new OmsiTripLeg { From = from, To = to };
                var existing = timetable.StationLinks.Find(link => link.FromStopId == from.ObjectId && link.ToStopId == to.ObjectId);
                if (existing != null)
                {
                    leg.Existing = true;
                    leg.LengthMeters = existing.LengthMeters;
                    plan.Legs.Add(leg);
                    continue;
                }
                var fromPlaces = placeOf(from);
                var toPlaces = placeOf(to);
                if (fromPlaces.Count == 0 || toPlaces.Count == 0)
                {
                    plan.Problems.Add((fromPlaces.Count == 0 ? from.Name : to.Name) + " has no vehicle lane beside it.");
                    plan.Legs.Add(leg);
                    continue;
                }
                var route = network.Route(fromPlaces, toPlaces);
                if (route == null || route.Lanes.Count == 0)
                {
                    plan.Problems.Add("No way leads from " + from.Name + " to " + to.Name + ".");
                    plan.Legs.Add(leg);
                    continue;
                }
                var first = fromPlaces.Find(place => place.Lane == route.Lanes[0]) ?? fromPlaces[0];
                var last = toPlaces.Find(place => place.Lane == route.Lanes[route.Lanes.Count - 1]) ?? toPlaces[0];
                leg.LengthMeters = route.LengthMeters;
                leg.FromPosition = first.Position;
                leg.ToPosition = last.Position;
                leg.FromDistance = first.Distance;
                leg.ToDistance = last.Distance;
                foreach (var lane in route.Lanes)
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

        /// <summary><c>StnLinks.cfg</c> with the legs the timetable lacks added behind the others.</summary>
        public static byte[] AppendStationLinks(byte[] existing, IEnumerable<OmsiTripLeg> legs)
        {
            var text = new StringBuilder();
            foreach (var leg in legs)
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
