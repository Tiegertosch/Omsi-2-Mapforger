using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One line of the list of signal routes (ADR 105, M1): what the popup shows for a route, from the route and its findings alone.</summary>
    public sealed class OmsiSignalRouteRow
    {
        /// <summary>The route in <see cref="OmsiSignalRoutes.Routes"/>.</summary>
        public int RouteIndex { get; internal set; }

        /// <summary>The number the file gives the route (the "   n:" comment), or its place in the file counted from 1 when it has none.</summary>
        public int Number { get; internal set; }

        public int? SignalId { get; internal set; }

        /// <summary>The file name of the signal's object (without folders), or null when the map has no such signal.</summary>
        public string SignalFile { get; internal set; }

        /// <summary>The aspect the signal shows for the route (1 clear, 2 clear with reduced speed), or null.</summary>
        public int? Aspect { get; internal set; }

        /// <summary>The track pieces the route lists, one for each [entry]; a switch counts once for each branch.</summary>
        public int PieceCount { get; internal set; }

        public double? SpeedLimit { get; internal set; }
        public int? DistantSignalId { get; internal set; }
        public int? NextSignalId { get; internal set; }

        /// <summary>The findings of this route, in the order of the file, as sentences.</summary>
        public List<string> Problems { get; } = new List<string>();

        public bool HasProblems => Problems.Count > 0;
    }

    /// <summary>Where the track of a route lies on the map, for highlighting it.</summary>
    public sealed class OmsiSignalRouteShape
    {
        /// <summary>The polylines of the route's track pieces in world metres, one for each piece found; the paths of a spline or object when the installation gave them, else the spline's centre line.</summary>
        public List<OmsiMapPoint[]> Lines { get; } = new List<OmsiMapPoint[]>();

        /// <summary>The places of pieces that have no line to draw (an object without paths, or without its file), as markers.</summary>
        public List<OmsiMapPoint> Points { get; } = new List<OmsiMapPoint>();

        /// <summary>The entries the map has no element for.</summary>
        public int Missing { get; internal set; }

        public bool IsEmpty => Lines.Count == 0 && Points.Count == 0;
    }

    /// <summary>The list of signal routes and the geometry of one route (ADR 105, M1), without Unity so it is tested on its own.</summary>
    public static class OmsiSignalRouteList
    {
        /// <summary>
        /// The rows of the list in the order of the file (which is the order of the route numbers), or of one signal's routes when
        /// <paramref name="signalId"/> is given.
        /// </summary>
        public static List<OmsiSignalRouteRow> Rows(OmsiSignalRouteMap map, int? signalId = null)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            var problems = new Dictionary<int, List<string>>();
            foreach (var problem in map.Problems)
            {
                if (problem.RouteIndex < 0)
                    continue;
                if (!problems.TryGetValue(problem.RouteIndex, out var list))
                    problems[problem.RouteIndex] = list = new List<string>();
                list.Add(problem.Message);
            }

            var rows = new List<OmsiSignalRouteRow>();
            foreach (var route in map.Routes.Routes)
            {
                if (signalId.HasValue && route.SignalId != signalId)
                    continue;
                var resolved = map.Resolved.Count > route.Index ? map.Resolved[route.Index] : null;
                var row = new OmsiSignalRouteRow
                {
                    RouteIndex = route.Index,
                    Number = route.Number ?? route.Index + 1,
                    SignalId = route.SignalId,
                    SignalFile = FileNameOf(resolved?.Signal?.AssetPath),
                    Aspect = route.Aspect,
                    PieceCount = route.Entries.Count,
                    SpeedLimit = route.SpeedLimit,
                    DistantSignalId = route.DistantSignalId,
                    NextSignalId = route.NextSignalId
                };
                if (problems.TryGetValue(route.Index, out var found))
                    row.Problems.AddRange(found);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>The findings of the file that belong to no route (a section before the first route, a file that ends in a section).</summary>
        public static List<string> FileProblems(OmsiSignalRouteMap map)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            var result = new List<string>();
            foreach (var problem in map.Problems)
                if (problem.RouteIndex < 0)
                    result.Add(problem.Message);
            return result;
        }

        /// <summary>The number of routes with a finding, for the summary line.</summary>
        public static int RoutesWithProblems(OmsiSignalRouteMap map)
        {
            var seen = new HashSet<int>();
            foreach (var problem in map.Problems)
                if (problem.RouteIndex >= 0)
                    seen.Add(problem.RouteIndex);
            return seen.Count;
        }

        /// <summary>
        /// The track of a route: for each entry the lane of the path it names (<paramref name="pathsOfTile"/> gives the paths of a tile, such as
        /// <see cref="OmsiMapPaths.OfTile"/>), or the centre line of its spline when the paths are not known, or a point for an object.
        /// </summary>
        /// <param name="tileIndex">Index in <see cref="OmsiMapSummary.Tiles"/>.</param>
        /// <param name="pathsOfTile">The paths of that tile; null or an empty list when they cannot be read.</param>
        public static OmsiSignalRouteShape Shape(OmsiResolvedSignalRoute route, OmsiMapSummary summary, Func<int, IReadOnlyList<OmsiMapPath>> pathsOfTile)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var shape = new OmsiSignalRouteShape();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in route.Entries)
            {
                var place = item.Element;
                if (!item.Entry.IsValid || place == null)
                {
                    shape.Missing++;
                    continue;
                }
                // A switch lists one entry for each branch; the same path is drawn once.
                if (!seen.Add(place.TileIndex + "/" + place.Id + "/" + item.Entry.Path))
                    continue;
                var found = false;
                var paths = pathsOfTile?.Invoke(place.TileIndex);
                if (paths != null)
                    foreach (var path in paths)
                    {
                        if (path.ElementId != place.Id || path.PathIndex != item.Entry.Path || path.TileX != place.TileX || path.TileY != place.TileY ||
                            path.OnSpline != (place.Kind == OmsiMapElementKind.Spline) || path.Points == null || path.Points.Length < 2)
                            continue;
                        shape.Lines.Add(path.Points);
                        found = true;
                    }
                if (found)
                    continue;
                if (place.Kind == OmsiMapElementKind.Spline && place.Spline != null)
                {
                    var line = OmsiMapGeometry.BuildSplinePolyline(summary.Tiles[place.TileIndex], place.Spline, summary.TileSizeMeters);
                    if (line.Length >= 2)
                    {
                        shape.Lines.Add(line);
                        continue;
                    }
                }
                shape.Points.Add(place.World);
            }
            return shape;
        }

        /// <summary>The tiles whose paths <see cref="Shape"/> asks for, each once.</summary>
        public static List<int> TilesOf(OmsiResolvedSignalRoute route)
        {
            var tiles = new List<int>();
            foreach (var item in route.Entries)
                if (item.Element != null && !tiles.Contains(item.Element.TileIndex))
                    tiles.Add(item.Element.TileIndex);
            return tiles;
        }

        /// <summary>The smallest box around the points of a shape and the given places, in world metres; false when there is nothing.</summary>
        public static bool Bounds(OmsiSignalRouteShape shape, IEnumerable<OmsiMapPoint> places, out OmsiMapPoint min, out OmsiMapPoint max)
        {
            var any = false;
            double minX = 0.0, minY = 0.0, maxX = 0.0, maxY = 0.0;
            void Add(OmsiMapPoint point)
            {
                if (!any)
                {
                    minX = maxX = point.X;
                    minY = maxY = point.Y;
                    any = true;
                    return;
                }
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            if (shape != null)
            {
                foreach (var line in shape.Lines)
                    foreach (var point in line)
                        Add(point);
                foreach (var point in shape.Points)
                    Add(point);
            }
            if (places != null)
                foreach (var point in places)
                    Add(point);
            min = new OmsiMapPoint(minX, minY);
            max = new OmsiMapPoint(maxX, maxY);
            return any;
        }

        private static string FileNameOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;
            var name = Path.GetFileName(assetPath.Replace('\\', '/'));
            return name.Length == 0 ? null : name;
        }
    }
}
