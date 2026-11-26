using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    public enum OmsiMapElementKind
    {
        Object,
        AttachedObject,
        SplineAttachment,
        Spline
    }

    /// <summary>Where an element of the map with an ID stands: its tile and a point for markers.</summary>
    public sealed class OmsiMapElementPlace
    {
        public int Id { get; internal set; }
        public OmsiMapElementKind Kind { get; internal set; }
        public string AssetPath { get; internal set; }

        /// <summary>The tile in <see cref="OmsiMapSummary.Tiles"/>, which is also the tile index of the [map] list.</summary>
        public int TileIndex { get; internal set; }

        public int TileX { get; internal set; }
        public int TileY { get; internal set; }

        /// <summary>
        /// Tile-local metres: an object's origin, the middle of a spline, the first object of a row along a spline, or for an attached object the
        /// place of the object it hangs on (the attachment point itself needs the parent's .sco).
        /// </summary>
        public double LocalX { get; internal set; }

        public double LocalY { get; internal set; }

        /// <summary>The point in world metres, as <see cref="OmsiMapGeometry.TileLocalToWorld"/> gives it.</summary>
        public OmsiMapPoint World { get; internal set; }

        /// <summary>The spline record for <see cref="OmsiMapElementKind.Spline"/>, to draw it; null otherwise.</summary>
        public OmsiSplineRecord Spline { get; internal set; }
    }

    /// <summary>An entry of a route with the element it names.</summary>
    public sealed class OmsiResolvedSignalRouteEntry
    {
        public OmsiSignalRouteEntry Entry { get; internal set; }

        /// <summary>The element on the entry's tile, or where else on the map it stands; null when the map has no element with the ID.</summary>
        public OmsiMapElementPlace Element { get; internal set; }

        /// <summary>The path number the tile gives the entry's path, or null when it was not checked.</summary>
        public int? ExpectedTilePath { get; internal set; }

        /// <summary>
        /// True when the path number could not be checked because a file before the element in its tile has an unknown number of paths; then
        /// <see cref="ExpectedTilePath"/> is null and <see cref="IsConsistent"/> only says the other checks passed (ADR 105, review of M2).
        /// </summary>
        public bool PathNumberUnchecked { get; internal set; }

        /// <summary>True when the element stands on the named tile and no check found a problem.</summary>
        public bool IsConsistent { get; internal set; }
    }

    /// <summary>A route with its signals and track pieces found on the map.</summary>
    public sealed class OmsiResolvedSignalRoute
    {
        public OmsiSignalRoute Route { get; internal set; }
        public OmsiMapElementPlace Signal { get; internal set; }
        public OmsiMapElementPlace DistantSignal { get; internal set; }
        public OmsiMapElementPlace NextSignal { get; internal set; }
        public List<OmsiResolvedSignalRouteEntry> Entries { get; } = new List<OmsiResolvedSignalRouteEntry>();

        /// <summary>The tiles the route touches: its signal's and its entries', each once, signal first.</summary>
        public List<int> TileIndices { get; } = new List<int>();
    }

    /// <summary>
    /// The signal routes laid onto the map (ADR 105): for each route where its signal stands and which splines and objects (switches) its entries name, so a
    /// view can mark the signals and highlight the track of one route (e.g. with <see cref="OmsiPathNetwork.LanesOfPath"/> per entry). IDs the map does not
    /// have, tile indices that do not match and stale path numbers become problems.
    /// </summary>
    public sealed class OmsiSignalRouteMap
    {
        private readonly Dictionary<int, OmsiMapElementPlace> elements = new Dictionary<int, OmsiMapElementPlace>();
        private readonly Dictionary<int, string> unknownAssets = new Dictionary<int, string>();

        public OmsiSignalRoutes Routes { get; private set; }
        public List<OmsiResolvedSignalRoute> Resolved { get; } = new List<OmsiResolvedSignalRoute>();

        /// <summary>Every signal of a route that stands on the map, once, in the order of its first route.</summary>
        public List<OmsiMapElementPlace> Signals { get; } = new List<OmsiMapElementPlace>();

        /// <summary>The problems of reading the file followed by those of laying it onto the map.</summary>
        public List<OmsiSignalRouteProblem> Problems { get; } = new List<OmsiSignalRouteProblem>();

        /// <summary>The element with an ID; the first one when a map has the ID twice.</summary>
        public OmsiMapElementPlace FindElement(int id)
        {
            return elements.TryGetValue(id, out var place) ? place : null;
        }

        /// <param name="source">Where the tile files are read for the path numbers; null skips that check.</param>
        /// <param name="pathCount">How many paths an asset has (e.g. <see cref="OmsiAssetPathCounter.Count"/>); null skips the path checks.</param>
        public static OmsiSignalRouteMap Resolve(OmsiSignalRoutes routes, OmsiMapSummary summary, IOmsiFileSource source, Func<string, int?> pathCount)
        {
            if (routes == null)
                throw new ArgumentNullException(nameof(routes));
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            var map = new OmsiSignalRouteMap { Routes = routes };
            map.Problems.AddRange(routes.Problems);
            if (routes.Routes.Count == 0)
                return map;
            map.IndexElements(summary);

            var tileNumbers = new Dictionary<int, Dictionary<int, int>>();
            var signals = new HashSet<int>();
            foreach (var route in routes.Routes)
            {
                var resolved = new OmsiResolvedSignalRoute { Route = route };
                map.Resolved.Add(resolved);
                var label = OmsiSignalRoutes.Describe(route);
                resolved.Signal = map.Signal(route.SignalId, route, OmsiSignalRouteProblemKind.UnknownSignal, label + ": the map has no signal ");
                resolved.DistantSignal = map.Signal(route.DistantSignalId, route, OmsiSignalRouteProblemKind.UnknownDistantSignal, label + ": the map has no distant signal ");
                resolved.NextSignal = map.Signal(route.NextSignalId, route, OmsiSignalRouteProblemKind.UnknownNextSignal, label + ": the map has no next signal ");
                if (resolved.Signal != null)
                {
                    resolved.TileIndices.Add(resolved.Signal.TileIndex);
                    if (signals.Add(resolved.Signal.Id))
                        map.Signals.Add(resolved.Signal);
                }

                for (var index = 0; index < route.Entries.Count; index++)
                {
                    var entry = route.Entries[index];
                    var item = new OmsiResolvedSignalRouteEntry { Entry = entry };
                    resolved.Entries.Add(item);
                    if (!entry.IsValid)
                        continue;
                    item.IsConsistent = map.CheckEntry(item, route, index, summary, source, pathCount, tileNumbers);
                    if (item.Element != null && !resolved.TileIndices.Contains(item.Element.TileIndex))
                        resolved.TileIndices.Add(item.Element.TileIndex);
                }
            }
            return map;
        }

        private bool CheckEntry(OmsiResolvedSignalRouteEntry item, OmsiSignalRoute route, int index, OmsiMapSummary summary, IOmsiFileSource source,
            Func<string, int?> pathCount, Dictionary<int, Dictionary<int, int>> tileNumbers)
        {
            var entry = item.Entry;
            var label = OmsiSignalRoutes.Describe(route) + ", entry " + index.ToString(CultureInfo.InvariantCulture) + " (element " +
                        entry.ElementId.ToString(CultureInfo.InvariantCulture) + ")";
            var place = FindElement(entry.ElementId);
            if (entry.TileIndex >= 0 && entry.TileIndex < summary.Tiles.Count && (place == null || place.TileIndex != entry.TileIndex))
                // The ID may stand twice on the map; the entry's own tile wins.
                place = FindOnTile(summary, entry.TileIndex, entry.ElementId) ?? place;
            item.Element = place;
            if (place == null)
            {
                Add(OmsiSignalRouteProblemKind.UnknownElement, route.Index, index, label + ": the map has no element with this ID.");
                return false;
            }
            if (entry.TileIndex < 0 || entry.TileIndex >= summary.Tiles.Count)
            {
                Add(OmsiSignalRouteProblemKind.UnknownTile, route.Index, index, label + ": the map has no tile " + Int(entry.TileIndex) + ".");
                return false;
            }
            if (place.TileIndex != entry.TileIndex)
            {
                Add(OmsiSignalRouteProblemKind.ElementOnOtherTile, route.Index, index,
                    label + ": the route names tile " + Int(entry.TileIndex) + ", the element stands on tile " + Int(place.TileIndex) + ".");
                return false;
            }
            if (pathCount == null)
                return true;

            var paths = pathCount(place.AssetPath);
            if (paths.HasValue && (entry.Path < 0 || entry.Path >= paths.Value))
            {
                Add(OmsiSignalRouteProblemKind.PathOutOfRange, route.Index, index,
                    label + ": path " + Int(entry.Path) + " of " + place.AssetPath + ", which has " + Int(paths.Value) + ".");
                return false;
            }
            if (source == null)
                return true;
            if (!tileNumbers.TryGetValue(entry.TileIndex, out var numbers))
            {
                var tile = summary.Tiles[entry.TileIndex];
                numbers = null;
                string unknownAsset = null;
                if (tile.IsAvailable && source.FileExists(tile.FileName))
                    numbers = OmsiTilePathNumbers.Of(OmsiRecordFile.Parse(source.ReadAllBytes(tile.FileName), OmsiTileRecordLayouts.Instance), pathCount, out unknownAsset);
                tileNumbers[entry.TileIndex] = numbers;
                unknownAssets[entry.TileIndex] = unknownAsset;
            }
            if (numbers == null || !numbers.TryGetValue(entry.ElementId, out var first))
            {
                // Behind a file with an unknown number of paths nothing can be told; attachments have no paths of their own in the tile's count.
                item.PathNumberUnchecked = numbers != null && unknownAssets[entry.TileIndex] != null && place.Kind != OmsiMapElementKind.AttachedObject &&
                                           place.Kind != OmsiMapElementKind.SplineAttachment;
                return true;
            }
            item.ExpectedTilePath = first + entry.Path;
            if (item.ExpectedTilePath.Value == entry.TilePath)
                return true;
            Add(OmsiSignalRouteProblemKind.PathNumberMismatch, route.Index, index,
                label + ": path number " + Int(entry.TilePath) + " in the tile, the tile gives it " + Int(item.ExpectedTilePath.Value) + ".");
            return false;
        }

        private OmsiMapElementPlace Signal(int? id, OmsiSignalRoute route, OmsiSignalRouteProblemKind kind, string message)
        {
            if (!id.HasValue)
                return null;
            var place = FindElement(id.Value);
            if (place == null)
                Add(kind, route.Index, -1, message + Int(id.Value) + ".");
            return place;
        }

        private void Add(OmsiSignalRouteProblemKind kind, int route, int entry, string message)
        {
            Problems.Add(new OmsiSignalRouteProblem(kind, route, entry, message));
        }

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private void IndexElements(OmsiMapSummary summary)
        {
            for (var index = 0; index < summary.Tiles.Count; index++)
                foreach (var place in PlacesOf(summary, index))
                    if (!elements.ContainsKey(place.Id))
                        elements.Add(place.Id, place);
        }

        private static OmsiMapElementPlace FindOnTile(OmsiMapSummary summary, int tileIndex, int id)
        {
            foreach (var place in PlacesOf(summary, tileIndex))
                if (place.Id == id)
                    return place;
            return null;
        }

        private static IEnumerable<OmsiMapElementPlace> PlacesOf(OmsiMapSummary summary, int tileIndex)
        {
            var tile = summary.Tiles[tileIndex];
            var size = summary.TileSizeMeters;
            var objects = new Dictionary<int, OmsiMapPoint>();
            foreach (var item in tile.Objects)
            {
                objects[item.Id] = new OmsiMapPoint(item.LocalX, item.LocalY);
                yield return Place(tile, tileIndex, size, item.Id, OmsiMapElementKind.Object, item.AssetPath, item.LocalX, item.LocalY, null);
            }
            // Attached objects follow their parent in the file, so a parent is known before its children.
            foreach (var item in tile.AttachedObjects)
            {
                if (!objects.TryGetValue(item.ParentId, out var parent))
                    continue;
                objects[item.Id] = parent;
                yield return Place(tile, tileIndex, size, item.Id, OmsiMapElementKind.AttachedObject, item.AssetPath, parent.X, parent.Y, null);
            }
            foreach (var item in tile.Splines)
            {
                var length = item.LengthMeters > 0.0 && !double.IsNaN(item.LengthMeters) && !double.IsInfinity(item.LengthMeters) ? item.LengthMeters : 0.0;
                OmsiMapGeometry.GetLocalFrameAt(item, length / 2.0, out var middle, out _);
                yield return Place(tile, tileIndex, size, item.Id, OmsiMapElementKind.Spline, item.AssetPath, middle.X, middle.Y, item);
            }
            foreach (var item in tile.SplineAttachments)
            {
                if (item.SplineIndex < 0 || item.SplineIndex >= tile.Splines.Count)
                    continue;
                OmsiMapGeometry.GetLocalFrameAt(tile.Splines[item.SplineIndex], item.StartDistance, out var point, out var heading);
                // Heading 0 runs along +Y and grows clockwise, so the right is (cos, -sin).
                yield return Place(tile, tileIndex, size, item.Id, OmsiMapElementKind.SplineAttachment, item.AssetPath,
                    point.X + Math.Cos(heading) * item.LateralOffset, point.Y - Math.Sin(heading) * item.LateralOffset, null);
            }
        }

        private static OmsiMapElementPlace Place(OmsiTileSummary tile, int tileIndex, double size, int id, OmsiMapElementKind kind, string asset, double x, double y,
            OmsiSplineRecord spline)
        {
            return new OmsiMapElementPlace
            {
                Id = id, Kind = kind, AssetPath = asset, TileIndex = tileIndex, TileX = tile.X, TileY = tile.Y, LocalX = x, LocalY = y,
                World = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, x, y, size), Spline = spline
            };
        }
    }
}
