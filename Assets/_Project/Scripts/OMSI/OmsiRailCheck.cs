using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    public enum OmsiRailFindingKind
    {
        /// <summary>P3: [switch] gives another number of positions than the [switchdir] of the paths use, or a position is not below it. An error.</summary>
        SwitchPositionCount,

        /// <summary>P3: the number after [switch] is missing, does not read or is not above 0; openOMSI then reads 0 positions. An error.</summary>
        SwitchCountUnreadable,

        /// <summary>P4: a [switchdir] comes before the first [path] of the .sco and belongs to no path. An error.</summary>
        SwitchDirectionBeforePath,

        /// <summary>P1: a rail path a signal route runs over ends without a rail path going on there.</summary>
        OpenRailEnd,

        /// <summary>P2: a switch object has path ends no track joins.</summary>
        OpenSwitchBranch,

        /// <summary>P6: a signal route runs over paths of two positions of the same switch.</summary>
        RouteInTwoPositions,

        /// <summary>P7: the track branches where no switch object is, so a train cannot set which way it goes.</summary>
        BranchWithoutSwitch
    }

    /// <summary>One finding of <see cref="OmsiRailCheck"/>; which fields are set depends on <see cref="Kind"/>.</summary>
    public sealed class OmsiRailFinding
    {
        public OmsiRailFindingKind Kind { get; internal set; }

        /// <summary>True for faults in a .sco (P3, P4); the others are warnings.</summary>
        public bool IsError => Kind == OmsiRailFindingKind.SwitchPositionCount || Kind == OmsiRailFindingKind.SwitchCountUnreadable ||
                               Kind == OmsiRailFindingKind.SwitchDirectionBeforePath;

        /// <summary>The .sco of a P3 (also an unreadable count) or P4 finding, or of the switch of a P2 finding.</summary>
        public string AssetPath { get; internal set; }

        public int TileX { get; internal set; }
        public int TileY { get; internal set; }

        /// <summary>The spline or object the finding is at.</summary>
        public int ElementId { get; internal set; }

        public bool OnSpline { get; internal set; }
        public int PathIndex { get; internal set; }

        /// <summary>Where the finding is, in metres from the corner of tile <see cref="TileX"/>, <see cref="TileY"/>.</summary>
        public double LocalX { get; internal set; }

        public double LocalY { get; internal set; }

        /// <summary>P1: the first signal route over the path; P6: the route. -1 otherwise.</summary>
        public int RouteIndex { get; internal set; } = -1;

        /// <summary>P3: the number [switch] gives; P2 and P4: how many ends or [switchdir] are meant.</summary>
        public int Count { get; internal set; }

        /// <summary>P3: the distinct positions of the paths; P6: the positions the route uses; P2: the paths with an open end.</summary>
        public List<int> Numbers { get; } = new List<int>();

        /// <summary>P1: the distance to the nearest end of another rail path when it is below <see cref="OmsiRailCheck.GapMeters"/>, else null.</summary>
        public double? NearestMeters { get; internal set; }

        /// <summary>P1: the rail path whose end is nearest, when <see cref="NearestMeters"/> is set.</summary>
        public OmsiMapPath Nearest { get; internal set; }

        /// <summary>P7: the path the track comes from and the paths it branches into.</summary>
        public OmsiMapPath From { get; internal set; }

        public List<OmsiMapPath> Branches { get; } = new List<OmsiMapPath>();

        /// <summary>P7: how far the farthest branch starts from where the track comes in.</summary>
        public double StartOffsetMeters { get; internal set; }
    }

    /// <summary>
    /// Checks of rail paths and switches (ADR 105, M5). A switch is an object whose .sco has [switch]; the [switchdir] after a
    /// [path] gives the position in which trains take it, as openOMSI reads it. Nothing is written.
    /// </summary>
    public static class OmsiRailCheck
    {
        /// <summary>An open rail end with another rail end nearer than this is reported as a gap rather than the end of the track.</summary>
        public const double GapMeters = 10.0;

        /// <summary>A branch starting farther than this from where the track comes in is joined only by the reach of <see cref="OmsiMapPaths.LinkMeters"/>.</summary>
        public const double OffsetMeters = 0.5;

        public const int RailKind = 2;

        /// <summary>P3 and P4 for one .sco; nothing when it is no switch and has no [switchdir] before its paths.</summary>
        public static void CheckSceneryObject(string assetPath, OmsiSceneryObject sceneryObject, List<OmsiRailFinding> into)
        {
            if (sceneryObject == null)
                throw new ArgumentNullException(nameof(sceneryObject));
            if (into == null)
                throw new ArgumentNullException(nameof(into));
            if (sceneryObject.StraySwitchDirections > 0)
                into.Add(new OmsiRailFinding { Kind = OmsiRailFindingKind.SwitchDirectionBeforePath, AssetPath = assetPath, Count = sceneryObject.StraySwitchDirections });
            if (!sceneryObject.IsSwitch)
                return;
            var positions = new SortedSet<int>();
            foreach (var path in sceneryObject.Paths)
                if (path.SwitchPosition.HasValue)
                    positions.Add(path.SwitchPosition.Value);
            var count = sceneryObject.SwitchPositions.Value;
            if (count <= 0)
            {
                into.Add(new OmsiRailFinding { Kind = OmsiRailFindingKind.SwitchCountUnreadable, AssetPath = assetPath });
                return;
            }
            var outOfRange = false;
            foreach (var position in positions)
                outOfRange |= position < 0 || position >= count;
            if (positions.Count == count && !outOfRange)
                return;
            var finding = new OmsiRailFinding { Kind = OmsiRailFindingKind.SwitchPositionCount, AssetPath = assetPath, Count = count };
            finding.Numbers.AddRange(positions);
            into.Add(finding);
        }

        /// <summary>P1, P2, P6 and P7 over the paths of a map.</summary>
        /// <param name="paths">The paths of all tiles of the map (<see cref="OmsiMapPaths.OfTile"/>); only rail paths are looked at, and their open ends are marked anew.</param>
        /// <param name="routes">The signal routes of the map, or null.</param>
        public static List<OmsiRailFinding> CheckTrack(OmsiMapSummary summary, IList<OmsiMapPath> paths, OmsiSignalRoutes routes)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));
            var findings = new List<OmsiRailFinding>();
            var rails = new List<OmsiMapPath>();
            foreach (var path in paths)
                if (path.Kind == RailKind && path.Points != null && path.Points.Length >= 2)
                    rails.Add(path);
            if (rails.Count == 0)
                return findings;
            OmsiMapPaths.FindOpenEnds(rails);
            var byKey = new Dictionary<string, OmsiMapPath>(StringComparer.Ordinal);
            foreach (var rail in rails)
                byKey[Key(rail.TileX, rail.TileY, rail.ElementId, rail.PathIndex)] = rail;

            // The rail paths the signal routes run over, with the first route over each.
            var firstRoute = new Dictionary<OmsiMapPath, int>();
            if (routes != null)
                foreach (var route in routes.Routes)
                    foreach (var entry in route.Entries)
                    {
                        var path = PathOf(summary, byKey, entry);
                        if (path != null && !firstRoute.ContainsKey(path))
                            firstRoute[path] = route.Index;
                    }

            OpenRailEnds(summary, rails, firstRoute, findings);
            OpenSwitchBranches(summary, rails, findings);
            if (routes != null)
                RoutesInTwoPositions(summary, byKey, routes, findings);
            BranchesWithoutSwitch(summary, rails, findings);
            return findings;
        }

        /// <summary>P1: open ends of rail paths a route runs over, unless the end lies outside the tiles of the map.</summary>
        private static void OpenRailEnds(OmsiMapSummary summary, List<OmsiMapPath> rails, Dictionary<OmsiMapPath, int> firstRoute, List<OmsiRailFinding> findings)
        {
            var size = summary.TileSizeMeters;
            var mapTiles = MapTiles(summary);
            foreach (var rail in rails)
            {
                if (!(rail.OpenStart || rail.OpenEnd) || !firstRoute.TryGetValue(rail, out var route))
                    continue;
                foreach (var atEnd in new[] { false, true })
                {
                    if (atEnd ? !rail.OpenEnd : !rail.OpenStart)
                        continue;
                    var point = atEnd ? rail.Points[rail.Points.Length - 1] : rail.Points[0];
                    // Beyond the edge of the map the track goes on in tiles the map does not have.
                    if (!InMap(mapTiles, point, size))
                        continue;
                    var finding = new OmsiRailFinding
                    {
                        Kind = OmsiRailFindingKind.OpenRailEnd, TileX = rail.TileX, TileY = rail.TileY, ElementId = rail.ElementId, OnSpline = rail.OnSpline,
                        PathIndex = rail.PathIndex, LocalX = point.X - rail.TileX * size, LocalY = point.Y - rail.TileY * size, RouteIndex = route
                    };
                    var nearest = GapMeters;
                    foreach (var other in rails)
                    {
                        if (other == rail)
                            continue;
                        foreach (var end in new[] { other.Points[0], other.Points[other.Points.Length - 1] })
                        {
                            var distance = Distance(end, point);
                            if (distance < nearest)
                            {
                                nearest = distance;
                                finding.Nearest = other;
                                finding.NearestMeters = distance;
                            }
                        }
                    }
                    findings.Add(finding);
                }
            }
        }

        /// <summary>P2: one finding per switch object with open path ends, counting only ends inside the tiles of the map (as P1).</summary>
        private static void OpenSwitchBranches(OmsiMapSummary summary, List<OmsiMapPath> rails, List<OmsiRailFinding> findings)
        {
            var size = summary.TileSizeMeters;
            var mapTiles = MapTiles(summary);
            var byObject = new Dictionary<string, OmsiRailFinding>(StringComparer.Ordinal);
            foreach (var rail in rails)
            {
                if (!rail.OnSwitch)
                    continue;
                var openStart = rail.OpenStart && InMap(mapTiles, rail.Points[0], size);
                var openEnd = rail.OpenEnd && InMap(mapTiles, rail.Points[rail.Points.Length - 1], size);
                if (!openStart && !openEnd)
                    continue;
                var key = Key(rail.TileX, rail.TileY, rail.ElementId, 0);
                if (!byObject.TryGetValue(key, out var finding))
                {
                    var point = openStart ? rail.Points[0] : rail.Points[rail.Points.Length - 1];
                    finding = new OmsiRailFinding
                    {
                        Kind = OmsiRailFindingKind.OpenSwitchBranch, TileX = rail.TileX, TileY = rail.TileY, ElementId = rail.ElementId, PathIndex = rail.PathIndex,
                        LocalX = point.X - rail.TileX * size, LocalY = point.Y - rail.TileY * size, AssetPath = AssetOf(summary, rail)
                    };
                    byObject[key] = finding;
                    findings.Add(finding);
                }
                finding.Count += (openStart ? 1 : 0) + (openEnd ? 1 : 0);
                finding.Numbers.Add(rail.PathIndex);
            }
        }

        /// <summary>P6: a route over paths of different positions of one switch; paths without a position are left out.</summary>
        private static void RoutesInTwoPositions(OmsiMapSummary summary, Dictionary<string, OmsiMapPath> byKey, OmsiSignalRoutes routes, List<OmsiRailFinding> findings)
        {
            var size = summary.TileSizeMeters;
            foreach (var route in routes.Routes)
            {
                var positions = new Dictionary<string, KeyValuePair<OmsiMapPath, SortedSet<int>>>(StringComparer.Ordinal);
                var order = new List<string>();
                foreach (var entry in route.Entries)
                {
                    var path = PathOf(summary, byKey, entry);
                    if (path == null || !path.OnSwitch || !path.SwitchPosition.HasValue)
                        continue;
                    var key = Key(path.TileX, path.TileY, path.ElementId, 0);
                    if (!positions.TryGetValue(key, out var pair))
                    {
                        positions[key] = pair = new KeyValuePair<OmsiMapPath, SortedSet<int>>(path, new SortedSet<int>());
                        order.Add(key);
                    }
                    pair.Value.Add(path.SwitchPosition.Value);
                }
                foreach (var key in order)
                {
                    var pair = positions[key];
                    if (pair.Value.Count < 2)
                        continue;
                    var path = pair.Key;
                    var finding = new OmsiRailFinding
                    {
                        Kind = OmsiRailFindingKind.RouteInTwoPositions, TileX = path.TileX, TileY = path.TileY, ElementId = path.ElementId, PathIndex = path.PathIndex,
                        LocalX = path.Points[0].X - path.TileX * size, LocalY = path.Points[0].Y - path.TileY * size, RouteIndex = route.Index,
                        AssetPath = AssetOf(summary, path)
                    };
                    finding.Numbers.AddRange(pair.Value);
                    findings.Add(finding);
                }
            }
        }

        /// <summary>P7: a rail lane with more than one following path, where neither it nor any of them belongs to a switch; one finding per place and heading.</summary>
        private static void BranchesWithoutSwitch(OmsiMapSummary summary, List<OmsiMapPath> rails, List<OmsiRailFinding> findings)
        {
            var size = summary.TileSizeMeters;
            var network = OmsiPathNetwork.Build(rails, RailKind);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var lane in network.Lanes)
            {
                var next = network.NextOf(lane);
                if (next.Count < 2 || lane.Path.OnSwitch)
                    continue;
                var branches = new List<OmsiLane>();
                var atSwitch = false;
                foreach (var candidate in next)
                {
                    atSwitch |= candidate.Path.OnSwitch;
                    if (!branches.Exists(branch => branch.Path == candidate.Path))
                        branches.Add(candidate);
                }
                if (atSwitch || branches.Count < 2)
                    continue;
                var points = lane.Geometry;
                var end = points[points.Length - 1];
                var before = points[points.Length - 2];
                var heading = Math.Atan2(end.X - before.X, end.Y - before.Y) * 180.0 / Math.PI;
                var sector = ((int)Math.Round(heading / 45.0) % 8 + 8) % 8;
                if (!seen.Add(Math.Round(end.X).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                              Math.Round(end.Y).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + sector))
                    continue;
                var finding = new OmsiRailFinding
                {
                    Kind = OmsiRailFindingKind.BranchWithoutSwitch, TileX = lane.Path.TileX, TileY = lane.Path.TileY, ElementId = lane.Path.ElementId,
                    OnSpline = lane.Path.OnSpline, PathIndex = lane.Path.PathIndex, LocalX = end.X - lane.Path.TileX * size, LocalY = end.Y - lane.Path.TileY * size,
                    From = lane.Path
                };
                foreach (var branch in branches)
                {
                    finding.Branches.Add(branch.Path);
                    finding.StartOffsetMeters = Math.Max(finding.StartOffsetMeters, Distance(branch.Geometry[0], end));
                }
                findings.Add(finding);
            }
        }

        private static OmsiMapPath PathOf(OmsiMapSummary summary, Dictionary<string, OmsiMapPath> byKey, OmsiSignalRouteEntry entry)
        {
            if (entry.TileIndex < 0 || entry.TileIndex >= summary.Tiles.Count || summary.Tiles[entry.TileIndex] == null)
                return null;
            var tile = summary.Tiles[entry.TileIndex];
            return byKey.TryGetValue(Key(tile.X, tile.Y, entry.ElementId, entry.Path), out var path) ? path : null;
        }

        private static string AssetOf(OmsiMapSummary summary, OmsiMapPath path)
        {
            foreach (var tile in summary.Tiles)
                if (tile != null && tile.X == path.TileX && tile.Y == path.TileY)
                    foreach (var record in tile.Objects)
                        if (record.Id == path.ElementId)
                            return record.AssetPath;
            return null;
        }

        private static string Key(int tileX, int tileY, int elementId, int pathIndex)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return tileX.ToString(culture) + "," + tileY.ToString(culture) + "," + elementId.ToString(culture) + "," + pathIndex.ToString(culture);
        }

        private static HashSet<long> MapTiles(OmsiMapSummary summary)
        {
            var mapTiles = new HashSet<long>();
            foreach (var tile in summary.Tiles)
                if (tile != null && tile.IsAvailable)
                    mapTiles.Add(TileKey(tile.X, tile.Y));
            return mapTiles;
        }

        private static bool InMap(HashSet<long> mapTiles, OmsiMapPoint point, double size)
        {
            return mapTiles.Contains(TileKey((int)Math.Floor(point.X / size), (int)Math.Floor(point.Y / size)));
        }

        private static long TileKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
    }
}
