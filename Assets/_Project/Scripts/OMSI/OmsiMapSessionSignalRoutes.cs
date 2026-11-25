using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Keeps signalroutes.cfg in step with the tiles (ADR 105, M2): every step that writes base tiles has the tile index and the path number of the
    /// [entry] records of those tiles worked out again and signalroutes.cfg written in the same step, so undo and redo take it along.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        private Func<string, int?> assetPathCount;
        private bool assetPathCountKnown;
        private string signalRoutesStamp;
        private OmsiSignalRoutes signalRoutesRead;
        private byte[] signalRoutesBytes;
        private string mapListStamp;
        private Dictionary<string, int> mapListRead;

        /// <summary>
        /// How many paths an object or spline file has, for the path numbers of signal routes; by default read from the installation that holds
        /// the map, or null when there is none (then path numbers are left as they are). Tools and tests may set their own.
        /// </summary>
        public Func<string, int?> AssetPathCount
        {
            get
            {
                if (!assetPathCountKnown)
                {
                    var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(Path.GetDirectoryName(Path.GetFullPath(GlobalConfigPath)));
                    assetPathCount = !string.IsNullOrEmpty(installation) && Directory.Exists(installation) ? new OmsiAssetPathCounter(installation).Count : (Func<string, int?>)null;
                    assetPathCountKnown = true;
                }
                return assetPathCount;
            }
            set
            {
                assetPathCount = value;
                assetPathCountKnown = true;
            }
        }

        /// <summary>
        /// Adds signalroutes.cfg to a step's files when the step changes the order, type or tile of a track piece a route names. Returns why the step
        /// cannot be done instead: a track piece of a route that is gone, a path the piece's new file does not have, or a tile whose paths cannot be
        /// numbered. A step that writes signalroutes.cfg itself, writes no base tile or changes the tile list is left alone.
        /// </summary>
        private List<string> KeepSignalRoutes(List<OmsiObjectChange.FileState> files)
        {
            var refusals = new List<string>();
            var workspace = RequireWorkspace();
            var tileStates = new List<OmsiObjectChange.FileState>();
            OmsiObjectChange.FileState configState = null;
            foreach (var file in files)
            {
                var path = file.RelativePath ?? string.Empty;
                if (string.Equals(path, OmsiSignalRoutes.FileName, StringComparison.OrdinalIgnoreCase))
                    return refusals;
                if (string.Equals(path, GlobalConfigFileName, StringComparison.OrdinalIgnoreCase))
                    configState = file;
                else if (path.EndsWith(".map", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("Chrono" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                         file.Before != null && file.After != null)
                    tileStates.Add(file);
            }
            if (tileStates.Count == 0 || !workspace.FileExists(OmsiSignalRoutes.FileName))
                return refusals;

            var routes = CurrentSignalRoutes(workspace, out var routeBytes);
            var namedTiles = new HashSet<int>();
            foreach (var route in routes.Routes)
                foreach (var entry in route.Entries)
                    if (entry.IsValid)
                        namedTiles.Add(entry.TileIndex);
            if (namedTiles.Count == 0)
                return refusals;

            var mapList = CurrentMapList(workspace);
            if (configState?.After != null && !SameMapList(mapList, MapListOf(configState.After)))
                // Adding or deleting a tile numbers the tiles anew; DeleteTile keeps the routes itself.
                return refusals;
            var changed = new Dictionary<int, OmsiObjectChange.FileState>();
            foreach (var state in tileStates)
                if (mapList.TryGetValue(state.RelativePath, out var tileIndex))
                    changed[tileIndex] = state;
            var touched = false;
            foreach (var tileIndex in changed.Keys)
                touched |= namedTiles.Contains(tileIndex);
            if (!touched)
                return refusals;

            var pathCount = AssetPathCount;
            var before = new Dictionary<int, List<KeyValuePair<int, string>>>();
            var after = new Dictionary<int, List<KeyValuePair<int, string>>>();
            List<KeyValuePair<int, string>> Elements(Dictionary<int, List<KeyValuePair<int, string>>> cache, int tileIndex, bool afterStep)
            {
                if (!cache.TryGetValue(tileIndex, out var list))
                {
                    var state = changed[tileIndex];
                    list = TrackElementsOf(afterStep ? state.After : state.Before);
                    cache[tileIndex] = list;
                }
                return list;
            }

            var edited = false;
            foreach (var route in routes.Routes)
                foreach (var entry in route.Entries)
                {
                    if (!entry.IsValid || !changed.ContainsKey(entry.TileIndex))
                        continue;
                    var label = OmsiSignalRoutes.Describe(route);
                    var oldList = Elements(before, entry.TileIndex, false);
                    var newList = Elements(after, entry.TileIndex, true);
                    var oldPlace = IndexOf(oldList, entry.ElementId);
                    var newPlace = IndexOf(newList, entry.ElementId);
                    if (newPlace >= 0)
                    {
                        if (!PathExists(pathCount, newList[newPlace].Value, entry, label, refusals))
                            continue;
                        if (oldPlace < 0)
                            continue;
                        var delta = PrefixDelta(oldList, oldPlace, newList, newPlace, pathCount, out var unknownAsset);
                        if (!delta.HasValue)
                        {
                            // Left as it was, the entry could name another path; refused as a tile change is (review of M2).
                            refusals.Add(label + " runs over element " + Int(entry.ElementId) + ", whose path number in tile " + Int(entry.TileIndex) +
                                         " cannot be kept: the number of paths of " + unknownAsset + " is unknown" +
                                         (pathCount == null ? " without the OMSI installation's object and spline files." : "."));
                            continue;
                        }
                        if (delta.Value != 0)
                        {
                            entry.Record.SetField(3, Int(entry.TilePath + delta.Value));
                            edited = true;
                        }
                        continue;
                    }

                    // The piece left its tile: it follows into the tile of the step that holds it now.
                    var moved = false;
                    foreach (var other in changed.Keys)
                    {
                        if (other == entry.TileIndex)
                            continue;
                        var otherList = Elements(after, other, true);
                        var place = IndexOf(otherList, entry.ElementId);
                        if (place < 0)
                            continue;
                        moved = true;
                        if (!PathExists(pathCount, otherList[place].Value, entry, label, refusals))
                            break;
                        var first = FirstPathOf(otherList, place, pathCount);
                        if (!first.HasValue)
                        {
                            refusals.Add(label + " runs over element " + Int(entry.ElementId) + ", whose paths in its new tile cannot be numbered " +
                                         "without the OMSI installation's object and spline files.");
                            break;
                        }
                        entry.Record.SetField(2, Int(other));
                        entry.Record.SetField(3, Int(first.Value + entry.Path));
                        edited = true;
                        break;
                    }
                    if (!moved && oldPlace >= 0)
                        refusals.Add(label + " runs over element " + Int(entry.ElementId) + "; take it out of the route first.");
                }

            if (refusals.Count > 0)
            {
                // Nothing of this step reaches the file; the cached routes are read again next time.
                signalRoutesStamp = null;
                return refusals;
            }
            if (edited)
            {
                files.Add(new OmsiObjectChange.FileState { RelativePath = OmsiSignalRoutes.FileName, Before = routeBytes, After = routes.File.ToBytes() });
                signalRoutesStamp = null;
            }
            return refusals;
        }

        /// <summary>The routes as the workspace holds them, read again only when the file changed.</summary>
        private OmsiSignalRoutes CurrentSignalRoutes(OmsiMapWorkspace workspace, out byte[] bytes)
        {
            var stamp = workspace.Stamp(OmsiSignalRoutes.FileName);
            // The records are edited in place by a step, so a cached copy is only reused while no step touched it; the file is read only when it changed.
            if (stamp != null && stamp == signalRoutesStamp && signalRoutesRead != null && signalRoutesBytes != null)
            {
                bytes = signalRoutesBytes;
                return signalRoutesRead;
            }
            bytes = workspace.ReadAllBytes(OmsiSignalRoutes.FileName);
            signalRoutesRead = OmsiSignalRoutes.Parse(bytes);
            signalRoutesBytes = bytes;
            signalRoutesStamp = stamp;
            return signalRoutesRead;
        }

        private Dictionary<string, int> CurrentMapList(OmsiMapWorkspace workspace)
        {
            var stamp = workspace.Stamp(GlobalConfigFileName);
            if (stamp != null && stamp == mapListStamp && mapListRead != null)
                return mapListRead;
            mapListRead = MapListOf(workspace.ReadAllBytes(GlobalConfigFileName));
            mapListStamp = stamp;
            return mapListRead;
        }

        /// <summary>Each tile file of global.cfg's [map] list by its index there, which [entry] uses.</summary>
        private static Dictionary<string, int> MapListOf(byte[] config)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var record in OmsiRecordFile.Parse(config, OmsiGlobalConfigLayouts.Instance).Records)
            {
                if (!string.Equals(record.Name, "map", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (record.Fields.Count == 3)
                {
                    try
                    {
                        var path = OmsiRelativePath.Normalize(record.GetField(2));
                        if (!result.ContainsKey(path))
                            result.Add(path, index);
                    }
                    catch (ArgumentException)
                    {
                        // A path that does not read names no tile file; it still counts in the list.
                    }
                }
                index++;
            }
            return result;
        }

        private static bool SameMapList(Dictionary<string, int> first, Dictionary<string, int> second)
        {
            if (first.Count != second.Count)
                return false;
            foreach (var pair in first)
                if (!second.TryGetValue(pair.Key, out var index) || index != pair.Value)
                    return false;
            return true;
        }

        /// <summary>The objects and splines of a tile in file order, with their files: the elements whose paths the tile counts (ADR 097).</summary>
        internal static List<KeyValuePair<int, string>> TrackElementsOf(byte[] tileBytes)
        {
            var result = new List<KeyValuePair<int, string>>();
            foreach (var record in OmsiRecordFile.Parse(tileBytes, OmsiTileRecordLayouts.Instance).Records)
            {
                var name = record.Name;
                if (!string.Equals(name, "object", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "spline", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "spline_h", StringComparison.OrdinalIgnoreCase))
                    continue;
                var values = record.AllValues();
                if (values.Count < 3 || !int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    continue;
                result.Add(new KeyValuePair<int, string>(id, values[1]));
            }
            return result;
        }

        private static int IndexOf(List<KeyValuePair<int, string>> elements, int id)
        {
            for (var index = 0; index < elements.Count; index++)
                if (elements[index].Key == id)
                    return index;
            return -1;
        }

        /// <summary>False, with the reason, when the element's file is known to have no such path, as after a change of its type.</summary>
        private static bool PathExists(Func<string, int?> pathCount, string asset, OmsiSignalRouteEntry entry, string label, List<string> refusals)
        {
            var count = pathCount?.Invoke(asset);
            if (!count.HasValue || (entry.Path >= 0 && entry.Path < count.Value))
                return true;
            refusals.Add(label + " runs over path " + Int(entry.Path) + " of element " + Int(entry.ElementId) + ", which " + asset + " does not have (it has " +
                         Int(count.Value) + ").");
            return false;
        }

        /// <summary>
        /// How far the first path of an element moved in its tile: the paths of what came or went before it, or changed its file. Elements that stayed
        /// the same need no count; null when one that differs has an unknown number of paths.
        /// </summary>
        private static int? PrefixDelta(List<KeyValuePair<int, string>> before, int beforePlace, List<KeyValuePair<int, string>> after, int afterPlace,
            Func<string, int?> pathCount, out string unknownAsset)
        {
            unknownAsset = null;
            var balance = new Dictionary<KeyValuePair<int, string>, int>(new ElementComparer());
            for (var index = 0; index < beforePlace; index++)
                balance[before[index]] = (balance.TryGetValue(before[index], out var value) ? value : 0) - 1;
            for (var index = 0; index < afterPlace; index++)
                balance[after[index]] = (balance.TryGetValue(after[index], out var value) ? value : 0) + 1;
            var delta = 0;
            foreach (var pair in balance)
            {
                if (pair.Value == 0)
                    continue;
                var count = pathCount?.Invoke(pair.Key.Value);
                if (!count.HasValue)
                {
                    unknownAsset = pair.Key.Value;
                    return null;
                }
                delta += pair.Value * count.Value;
            }
            return delta;
        }

        private static int? FirstPathOf(List<KeyValuePair<int, string>> elements, int place, Func<string, int?> pathCount)
        {
            if (pathCount == null)
                return null;
            var number = 0;
            for (var index = 0; index < place; index++)
            {
                var count = pathCount(elements[index].Value);
                if (!count.HasValue)
                    return null;
                number += count.Value;
            }
            return number;
        }

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class ElementComparer : IEqualityComparer<KeyValuePair<int, string>>
        {
            public bool Equals(KeyValuePair<int, string> first, KeyValuePair<int, string> second)
            {
                return first.Key == second.Key && string.Equals(first.Value, second.Value, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(KeyValuePair<int, string> element)
            {
                return element.Key ^ StringComparer.OrdinalIgnoreCase.GetHashCode(element.Value ?? string.Empty);
            }
        }
    }
}
