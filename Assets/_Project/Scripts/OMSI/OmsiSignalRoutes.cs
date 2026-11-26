using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Field layouts of signalroutes.cfg (ADR 105). The "   n:" numbers before each [signalroute] are comments and stay in
    /// the trailer of the record before them, so the file is written back byte for byte.
    /// </summary>
    public sealed class OmsiSignalRouteLayouts : IOmsiRecordLayouts
    {
        public static readonly OmsiSignalRouteLayouts Instance = new OmsiSignalRouteLayouts();

        private static readonly Dictionary<string, int> FieldCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "signalroute", 1 },
            { "signal", 2 },
            { "entry", 4 },
            { "distsignal", 1 },
            { "nextsignal", 1 },
            { "speedlimit", 1 }
        };

        public int? GetFieldCount(string name, int version, IReadOnlyList<OmsiLine> lines, int firstField, out bool isComplete)
        {
            isComplete = true;
            return FieldCounts.TryGetValue(name, out var count) ? count : (int?)null;
        }
    }

    public enum OmsiSignalRouteProblemKind
    {
        /// <summary>A route section before the first [signalroute]; it belongs to no route.</summary>
        OutsideRoute,
        /// <summary>The file ends inside a section.</summary>
        Incomplete,
        /// <summary>A field is not the number it should be.</summary>
        InvalidValue,
        /// <summary>A route has a section twice that it can only have once, such as two [signal].</summary>
        DuplicateSection,
        /// <summary>A route without [signal].</summary>
        MissingSignal,
        UnknownSignal,
        UnknownDistantSignal,
        UnknownNextSignal,
        /// <summary>No element of the map has the entry's ID.</summary>
        UnknownElement,
        /// <summary>The entry's tile index names no tile of the map.</summary>
        UnknownTile,
        /// <summary>The element exists, but on another tile than the entry names.</summary>
        ElementOnOtherTile,
        /// <summary>The entry's path is not a path of the element's object or spline file.</summary>
        PathOutOfRange,
        /// <summary>The entry's path number in its tile is not the one the tile gives the element's path (ADR 097).</summary>
        PathNumberMismatch
    }

    /// <summary>Something in signalroutes.cfg that does not read or does not match the map.</summary>
    public sealed class OmsiSignalRouteProblem
    {
        public OmsiSignalRouteProblem(OmsiSignalRouteProblemKind kind, int routeIndex, int entryIndex, string message)
        {
            Kind = kind;
            RouteIndex = routeIndex;
            EntryIndex = entryIndex;
            Message = message;
        }

        public OmsiSignalRouteProblemKind Kind { get; }

        /// <summary>The route in <see cref="OmsiSignalRoutes.Routes"/>, or -1 outside a route.</summary>
        public int RouteIndex { get; }

        /// <summary>The entry in the route, or -1 when the problem is not about one entry.</summary>
        public int EntryIndex { get; }

        public string Message { get; }

        public override string ToString()
        {
            return Message;
        }
    }

    /// <summary>One [entry] of a route: a path of a spline or object, such as one branch of a switch.</summary>
    public sealed class OmsiSignalRouteEntry
    {
        public OmsiRecord Record { get; internal set; }
        public int ElementId { get; internal set; }

        /// <summary>The [path] of the element's .sli or .sco, from 0.</summary>
        public int Path { get; internal set; }

        /// <summary>The tile in the [map] list of global.cfg, from 0.</summary>
        public int TileIndex { get; internal set; }

        /// <summary>The number of the path among all paths of the tile (<see cref="OmsiTilePathNumbers"/>).</summary>
        public int TilePath { get; internal set; }

        /// <summary>False when a field does not read as a number; the values are then 0 where they failed.</summary>
        public bool IsValid { get; internal set; }
    }

    /// <summary>One [signalroute]: a signal, the aspect it shows for the route and the paths the route runs over.</summary>
    public sealed class OmsiSignalRoute
    {
        /// <summary>Place in <see cref="OmsiSignalRoutes.Routes"/>, from 0.</summary>
        public int Index { get; internal set; }

        /// <summary>The "   n:" comment before the route, or null when there is none. OMSI does not read it.</summary>
        public int? Number { get; internal set; }

        /// <summary>The value of [signalroute]; 0 or 1 on the stock maps, meaning open (ADR 105). openOMSI ignores it.</summary>
        public int Kind { get; internal set; }

        public int? SignalId { get; internal set; }

        /// <summary>The aspect the signal shows for this route: 1 clear, 2 clear with reduced speed on the stock maps.</summary>
        public int? Aspect { get; internal set; }

        public List<OmsiSignalRouteEntry> Entries { get; } = new List<OmsiSignalRouteEntry>();

        /// <summary>[distsignal]: the distant signal that repeats this signal's aspect.</summary>
        public int? DistantSignalId { get; internal set; }

        /// <summary>[nextsignal]: the next main signal, whose aspect this signal announces.</summary>
        public int? NextSignalId { get; internal set; }

        /// <summary>[speedlimit] in km/h; openOMSI only asks whether it is there.</summary>
        public double? SpeedLimit { get; internal set; }

        /// <summary>The [signalroute] record and every record of the route after it, in file order.</summary>
        public List<OmsiRecord> Records { get; } = new List<OmsiRecord>();

        public OmsiRecord RouteRecord => Records.Count > 0 ? Records[0] : null;
        public OmsiRecord SignalRecord { get; internal set; }
        public OmsiRecord DistantSignalRecord { get; internal set; }
        public OmsiRecord NextSignalRecord { get; internal set; }
        public OmsiRecord SpeedLimitRecord { get; internal set; }
    }

    /// <summary>
    /// The signal routes of a map from signalroutes.cfg (ADR 105), read only. The file stays an <see cref="OmsiRecordFile"/> that writes back byte for byte;
    /// what does not read becomes a problem instead of being dropped.
    /// </summary>
    public sealed class OmsiSignalRoutes
    {
        public const string FileName = "signalroutes.cfg";

        private readonly List<OmsiSignalRoute> routes = new List<OmsiSignalRoute>();

        private OmsiSignalRoutes(OmsiRecordFile file)
        {
            File = file;
        }

        /// <summary>The file, or null when the map has none.</summary>
        public OmsiRecordFile File { get; }

        public IReadOnlyList<OmsiSignalRoute> Routes => routes;

        public List<OmsiSignalRouteProblem> Problems { get; } = new List<OmsiSignalRouteProblem>();

        /// <summary>No routes, for a map without the file.</summary>
        public static OmsiSignalRoutes Empty()
        {
            return new OmsiSignalRoutes(null);
        }

        /// <summary>Reads the map's signalroutes.cfg; a missing file gives no routes.</summary>
        public static OmsiSignalRoutes Read(IOmsiFileSource source, string relativePath = FileName)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            return source.FileExists(relativePath) ? Parse(source.ReadAllBytes(relativePath)) : Empty();
        }

        public static OmsiSignalRoutes Parse(byte[] bytes)
        {
            var file = OmsiRecordFile.Parse(bytes, OmsiSignalRouteLayouts.Instance);
            var result = new OmsiSignalRoutes(file);
            OmsiSignalRoute route = null;
            IList<OmsiLine> before = file.Preamble;
            foreach (var record in file.Records)
            {
                var name = record.Name.ToLowerInvariant();
                var known = record.HasKnownLayout;
                if (name == "signalroute" && known)
                {
                    route = new OmsiSignalRoute { Index = result.routes.Count, Number = CommentNumber(before) };
                    result.routes.Add(route);
                }

                before = record.Trailer;
                if (!known)
                {
                    // Unknown sections stay as they are and belong to no route.
                    if (route != null)
                        route.Records.Add(record);
                    continue;
                }

                if (route == null)
                {
                    result.Add(OmsiSignalRouteProblemKind.OutsideRoute, -1, -1, "[" + record.Name + "] stands before the first [signalroute].");
                    continue;
                }

                route.Records.Add(record);
                if (!record.IsComplete)
                    result.Add(OmsiSignalRouteProblemKind.Incomplete, route.Index, -1, Describe(route) + ": the file ends inside [" + record.Name + "].");

                switch (name)
                {
                    case "signalroute":
                        route.Kind = result.Int(record, 0, route, -1) ?? 0;
                        break;
                    case "signal":
                        if (route.SignalRecord != null)
                            result.Add(OmsiSignalRouteProblemKind.DuplicateSection, route.Index, -1, Describe(route) + " has [signal] twice; the first counts.");
                        else
                        {
                            route.SignalRecord = record;
                            route.SignalId = result.Int(record, 0, route, -1);
                            route.Aspect = result.Int(record, 1, route, -1);
                        }
                        break;
                    case "entry":
                        var entryIndex = route.Entries.Count;
                        var element = result.Int(record, 0, route, entryIndex);
                        var path = result.Int(record, 1, route, entryIndex);
                        var tile = result.Int(record, 2, route, entryIndex);
                        var tilePath = result.Int(record, 3, route, entryIndex);
                        route.Entries.Add(new OmsiSignalRouteEntry
                        {
                            Record = record, ElementId = element ?? 0, Path = path ?? 0, TileIndex = tile ?? 0, TilePath = tilePath ?? 0,
                            IsValid = element.HasValue && path.HasValue && tile.HasValue && tilePath.HasValue
                        });
                        break;
                    case "distsignal":
                        if (route.DistantSignalRecord != null)
                            result.Add(OmsiSignalRouteProblemKind.DuplicateSection, route.Index, -1, Describe(route) + " has [distsignal] twice; the first counts.");
                        else
                        {
                            route.DistantSignalRecord = record;
                            route.DistantSignalId = result.Int(record, 0, route, -1);
                        }
                        break;
                    case "nextsignal":
                        if (route.NextSignalRecord != null)
                            result.Add(OmsiSignalRouteProblemKind.DuplicateSection, route.Index, -1, Describe(route) + " has [nextsignal] twice; the first counts.");
                        else
                        {
                            route.NextSignalRecord = record;
                            route.NextSignalId = result.Int(record, 0, route, -1);
                        }
                        break;
                    case "speedlimit":
                        if (route.SpeedLimitRecord != null)
                            result.Add(OmsiSignalRouteProblemKind.DuplicateSection, route.Index, -1, Describe(route) + " has [speedlimit] twice; the first counts.");
                        else
                        {
                            route.SpeedLimitRecord = record;
                            if (record.TryGetDouble(0, out var speed))
                                route.SpeedLimit = speed;
                            else if (record.Fields.Count > 0)
                                result.Add(OmsiSignalRouteProblemKind.InvalidValue, route.Index, -1,
                                    Describe(route) + ": [speedlimit] '" + record.GetField(0) + "' is not a number.");
                        }
                        break;
                }
            }

            foreach (var each in result.routes)
                if (each.SignalRecord == null)
                    result.Add(OmsiSignalRouteProblemKind.MissingSignal, each.Index, -1, Describe(each) + " has no [signal].");
            return result;
        }

        /// <summary>The routes of one signal, in file order.</summary>
        public List<OmsiSignalRoute> RoutesOfSignal(int signalId)
        {
            var result = new List<OmsiSignalRoute>();
            foreach (var route in routes)
                if (route.SignalId == signalId)
                    result.Add(route);
            return result;
        }

        /// <summary>Every signal with a route, once, in the order of its first route.</summary>
        public List<int> SignalIds()
        {
            var seen = new HashSet<int>();
            var result = new List<int>();
            foreach (var route in routes)
                if (route.SignalId.HasValue && seen.Add(route.SignalId.Value))
                    result.Add(route.SignalId.Value);
            return result;
        }

        internal static string Describe(OmsiSignalRoute route)
        {
            return "Signal route " + (route.Number ?? route.Index).ToString(CultureInfo.InvariantCulture) +
                   (route.SignalId.HasValue ? " (signal " + route.SignalId.Value.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
        }

        private void Add(OmsiSignalRouteProblemKind kind, int route, int entry, string message)
        {
            Problems.Add(new OmsiSignalRouteProblem(kind, route, entry, message));
        }

        private int? Int(OmsiRecord record, int index, OmsiSignalRoute route, int entry)
        {
            if (index >= record.Fields.Count)
                return null;
            if (record.TryGetInt(index, out var value))
                return value;
            Add(OmsiSignalRouteProblemKind.InvalidValue, route.Index, entry,
                Describe(route) + ": value " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of [" + record.Name + "] '" + record.GetField(index) + "' is not a whole number.");
            return null;
        }

        /// <summary>The number of a "   n:" line, the last non-blank line before a route.</summary>
        private static int? CommentNumber(IList<OmsiLine> lines)
        {
            var index = CommentLineIndex(lines);
            if (index < 0)
                return null;
            var text = lines[index].Text.Trim();
            return int.Parse(text.Substring(0, text.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture);
        }

        /// <summary>The place of the "   n:" line in the lines before a route, or -1 when the last non-blank line is no such comment.</summary>
        internal static int CommentLineIndex(IList<OmsiLine> lines)
        {
            for (var index = lines.Count - 1; index >= 0; index--)
            {
                var text = lines[index].Text.Trim();
                if (text.Length == 0)
                    continue;
                if (text.Length >= 2 && text[text.Length - 1] == ':' &&
                    int.TryParse(text.Substring(0, text.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return index;
                return -1;
            }
            return -1;
        }
    }
}
