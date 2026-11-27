using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A new signal route (ADR 105, M4): its track pieces in the way of travel, as a search gives them, and its values.</summary>
    public sealed class OmsiSignalRouteDraft
    {
        /// <summary>The value of [signalroute], 0 or more; OMSI's routes use 0 and 1, meaning open.</summary>
        public int Kind { get; set; }

        /// <summary>The aspect in [signal], 1 or more; null gives 2 with a speed limit and 1 without, as on the stock maps.</summary>
        public int? Aspect { get; set; }

        /// <summary>The track pieces, e.g. <see cref="OmsiSignalRouteWay.Entries"/>; each must lead on to the next.</summary>
        public List<OmsiSignalRouteDraftEntry> Entries { get; } = new List<OmsiSignalRouteDraftEntry>();

        /// <summary>[distsignal]: a signal object of the map, or null for none.</summary>
        public int? DistantSignal { get; set; }

        /// <summary>[nextsignal]: a signal object of the map, or null for none.</summary>
        public int? NextSignal { get; set; }

        /// <summary>[speedlimit] in km/h, above 0 and at most <see cref="OmsiMapSession.MaximumSignalRouteSpeed"/>, or null for none.</summary>
        public double? SpeedLimit { get; set; }
    }

    /// <summary>Creating signal routes (ADR 105, M4): one step each, the route added at the end of signalroutes.cfg, the rest of the file byte for byte.</summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>The line a signalroutes.cfg that MapWorks starts carries; not the mark of a map made with MapWorks (ADR 052), which an export would take along.</summary>
        internal const string SignalRoutesWrittenLine = "Written by OMSI MapWorks";

        /// <summary>
        /// The rails of the whole map for <see cref="OmsiSignalRouteFinder"/>, read with the object and spline files of the installation that holds the map;
        /// null without an installation. On Berlin-Spandau about 0.4 s.
        /// </summary>
        public OmsiPathNetwork SignalRouteRails(OmsiMapSummary summary = null)
        {
            var installation = InstallationRoot();
            return installation == null ? null : OmsiPathNetwork.ForMap(summary ?? Read(), installation, 2);
        }

        /// <summary>
        /// Finds the ways a route of a signal can take to a target (ADR 105, M4); a next-signal search stops at the objects whose file is a signal
        /// (<see cref="IsSignalAsset"/>).
        /// </summary>
        /// <param name="rails">The rails, e.g. from <see cref="SignalRouteRails"/>; null reads them.</param>
        /// <param name="signalsWithRoutes">The signals a next-signal search stops at first (<see cref="SignalIdsWithRoutes"/>); null reads signalroutes.cfg. A caller that searches on a worker thread passes it.</param>
        /// <exception cref="OmsiStepRefusedException">The rails cannot be read without the OMSI installation.</exception>
        public OmsiSignalRouteSearch FindSignalRouteWays(int signalId, OmsiSignalRouteTarget target, OmsiMapSummary summary = null, OmsiPathNetwork rails = null,
            OmsiSignalRouteSearchOptions options = null, ISet<int> signalsWithRoutes = null)
        {
            summary = summary ?? Read();
            rails = rails ?? SignalRouteRails(summary);
            if (rails == null)
                throw new OmsiStepRefusedException("Finding the way of a signal route needs the OMSI installation's object and spline files.");
            if (target == null || target.Kind != OmsiSignalRouteTargetKind.NextSignal || target.Signals != null || (options != null && options.NextSignalCandidates != null))
                return OmsiSignalRouteFinder.Find(summary, rails, signalId, target, IsSignalAsset, options);

            // The next signal is the next one that has a route of its own: OMSI's routes on Berlin-Spandau stop at those, not at a main signal that no train is
            // routed past. A map without such a signal, or a search that finds nothing this way, takes every signal.
            var withRoutes = signalsWithRoutes ?? SignalIdsWithRoutes();
            if (withRoutes.Count > 0)
            {
                var narrowed = CopyOf(options);
                narrowed.NextSignalCandidates = withRoutes;
                var search = OmsiSignalRouteFinder.Find(summary, rails, signalId, target, IsSignalAsset, narrowed);
                if (search.Found || search.Problem == OmsiSignalRouteSearchProblem.UnknownSignal || search.Problem == OmsiSignalRouteSearchProblem.NoTrackAtSignal)
                    return search;
            }
            return OmsiSignalRouteFinder.Find(summary, rails, signalId, target, IsSignalAsset, options);
        }

        /// <summary>The IDs of the signals that have a route in signalroutes.cfg as it is now.</summary>
        public HashSet<int> SignalIdsWithRoutes()
        {
            var result = new HashSet<int>();
            var workspace = RequireWorkspace();
            if (!workspace.FileExists(OmsiSignalRoutes.FileName))
                return result;
            foreach (var id in OmsiSignalRoutes.Parse(workspace.ReadAllBytes(OmsiSignalRoutes.FileName)).SignalIds())
                result.Add(id);
            return result;
        }

        private static OmsiSignalRouteSearchOptions CopyOf(OmsiSignalRouteSearchOptions options)
        {
            options = options ?? new OmsiSignalRouteSearchOptions();
            return new OmsiSignalRouteSearchOptions
            {
                StartOnSignalTrack = options.StartOnSignalTrack, SignalReachMeters = options.SignalReachMeters, MaximumWays = options.MaximumWays,
                MaximumLengthMeters = options.MaximumLengthMeters, MaximumEntries = options.MaximumEntries, MaximumSteps = options.MaximumSteps,
                OverlapPieces = options.OverlapPieces, NextSignalCandidates = options.NextSignalCandidates
            };
        }

        /// <summary>
        /// Adds a signal route as one undoable step, after the last route with the next "   n:" number, laid out as the OMSI editor writes it; starts
        /// signalroutes.cfg when the map has none. The tile path number of every entry comes from its tile (<see cref="OmsiTilePathNumbers"/>).
        /// </summary>
        /// <param name="summary">The map as it stands; null reads it.</param>
        /// <param name="rails">The rails to check that the entries lead on from one to the next; null reads those of the entries' tiles.</param>
        /// <exception cref="ArgumentException">A value is out of range, the signal is no signal, an entry names no path of the map, or the entries do not join.</exception>
        /// <exception cref="OmsiStepRefusedException">A path count or path number cannot be known, or the file cannot take the route.</exception>
        public OmsiSignalRouteChange CreateSignalRoute(int signalId, OmsiSignalRouteDraft draft, OmsiMapSummary summary = null, OmsiPathNetwork rails = null)
        {
            if (draft == null)
                throw new ArgumentNullException(nameof(draft));
            var label = "New signal route (signal " + Int(signalId) + ")";
            if (draft.Kind < 0)
                throw new ArgumentException(label + ": the kind must be 0 or more, not " + Int(draft.Kind) + ".", nameof(draft));
            if (draft.Aspect.HasValue && draft.Aspect.Value < 1)
                throw new ArgumentException(label + ": the aspect must be 1 or more, not " + Int(draft.Aspect.Value) + ".", nameof(draft));
            if (draft.SpeedLimit.HasValue && (double.IsNaN(draft.SpeedLimit.Value) || double.IsInfinity(draft.SpeedLimit.Value) || draft.SpeedLimit.Value <= 0.0 ||
                                              draft.SpeedLimit.Value > MaximumSignalRouteSpeed))
                throw new ArgumentException(label + ": the speed limit must be above 0 and at most " +
                                            MaximumSignalRouteSpeed.ToString("0", CultureInfo.InvariantCulture) + " km/h.", nameof(draft));
            if (draft.Entries.Count == 0)
                throw new ArgumentException(label + " needs at least one track piece.", nameof(draft));

            var workspace = RequireWorkspace();
            summary = summary ?? Read();
            var places = OmsiSignalRouteMap.ElementsOf(summary);
            CheckSignal(places, signalId, label, "signal");
            if (draft.DistantSignal.HasValue)
                CheckSignal(places, draft.DistantSignal.Value, label, "distant signal");
            if (draft.NextSignal.HasValue)
                CheckSignal(places, draft.NextSignal.Value, label, "next signal");

            var tilePaths = TilePathsOfEntries(draft.Entries, summary, workspace, label);
            CheckEntriesJoin(draft.Entries, summary, rails, label);

            // The file as it is, or a new one with the head the OMSI editor writes.
            byte[] before = null;
            OmsiSignalRoutes routes;
            if (workspace.FileExists(OmsiSignalRoutes.FileName))
            {
                before = workspace.ReadAllBytes(OmsiSignalRoutes.FileName);
                routes = OmsiSignalRoutes.Parse(before);
                if (!routes.File.IsExactRoundTrip)
                    throw new OmsiStepRefusedException(OmsiSignalRoutes.FileName + " does not read back exactly; it is left as it is.");
            }
            else
                routes = OmsiSignalRoutes.Parse(NewSignalRoutesFile(DateTime.Now));

            var after = AppendedRoute(routes, signalId, draft, tilePaths, label);
            var change = WriteSignalRoutes(OmsiSignalRouteChangeKind.Creation, routes.Routes.Count, before, after);
            if (change == null)
                throw new OmsiStepRefusedException(label + " left " + OmsiSignalRoutes.FileName + " as it was.");
            return change;
        }

        /// <summary>
        /// Gives a route new track pieces as one undoable step (ADR 105, M4), checked as for <see cref="CreateSignalRoute"/>; the signal, the values and
        /// the blank lines after the last entry stay. Null when the pieces are the same.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">No route has this index.</exception>
        /// <exception cref="OmsiStepRefusedException">The route's entries are not one block of the file, or as for <see cref="CreateSignalRoute"/>.</exception>
        public OmsiSignalRouteChange ReplaceSignalRouteTrack(int routeIndex, IList<OmsiSignalRouteDraftEntry> entries, OmsiMapSummary summary = null,
            OmsiPathNetwork rails = null)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            var routes = EditableSignalRoutes(out var before);
            if (routeIndex < 0 || routeIndex >= routes.Routes.Count)
                throw new ArgumentOutOfRangeException(nameof(routeIndex), routeIndex, "The map has " + routes.Routes.Count + " signal route(s).");
            var route = routes.Routes[routeIndex];
            var label = OmsiSignalRoutes.Describe(route);
            if (entries.Count == 0)
                throw new ArgumentException(label + " needs at least one track piece.", nameof(entries));
            summary = summary ?? Read();
            var tilePaths = TilePathsOfEntries(entries, summary, RequireWorkspace(), label);
            CheckEntriesJoin(entries, summary, rails, label);

            var file = routes.File;
            if (route.Entries.Count == 0)
                throw new OmsiStepRefusedException(label + " has no [entry] to replace; edit signalroutes.cfg by hand first.");
            var first = file.Records.IndexOf(route.Entries[0].Record);
            var last = route.Entries[route.Entries.Count - 1].Record;
            for (var index = 0; index < route.Entries.Count; index++)
            {
                var record = route.Entries[index].Record;
                if (file.Records.IndexOf(record) != first + index)
                    throw new OmsiStepRefusedException(label + " has other sections between its [entry] records; edit signalroutes.cfg by hand first.");
                if (record != last && Rest(record.Trailer).Count > 0)
                    throw new OmsiStepRefusedException(label + " has text after an [entry]; edit signalroutes.cfg by hand first.");
            }

            // The last entry keeps the blank lines and whatever follows them (the next route's "   n:" line).
            var closing = new List<OmsiLine>(last.Trailer);
            var endsWithoutBreak = closing.Count == 0 && last.LastLine.Ending.Length == 0;
            foreach (var each in route.Entries)
                file.RemoveRecord(each.Record);
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var record = file.CreateRecord("entry", new[]
                {
                    OmsiNumberFormat.Format(entry.ElementId), OmsiNumberFormat.Format(entry.Path), OmsiNumberFormat.Format(entry.TileIndex), OmsiNumberFormat.Format(tilePaths[index])
                });
                if (index == entries.Count - 1)
                {
                    record.Trailer.Clear();
                    record.Trailer.AddRange(closing);
                    if (endsWithoutBreak)
                        record.LastLine.Ending = string.Empty;
                }
                file.InsertRecord(first + index, record);
            }
            return WriteSignalRoutes(OmsiSignalRouteChangeKind.Edit, routeIndex, before, file.ToBytes());
        }

        /// <summary>The head of a signalroutes.cfg MapWorks starts: as the OMSI editor writes it, in Windows-1252 with CRLF.</summary>
        internal static byte[] NewSignalRoutesFile(DateTime createdAt)
        {
            return OmsiTextFile.Encode("-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\n" + SignalRoutesWrittenLine + "\r\nDate: " +
                                       createdAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + "\r\n\r\n", OmsiTextEncoding.Windows1252);
        }

        private string InstallationRoot()
        {
            var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(Path.GetDirectoryName(Path.GetFullPath(GlobalConfigPath)));
            return !string.IsNullOrEmpty(installation) && Directory.Exists(installation) ? installation : null;
        }

        /// <summary>The number of every entry's path among the paths of its tile; refuses where it cannot be known.</summary>
        private List<int> TilePathsOfEntries(IList<OmsiSignalRouteDraftEntry> entries, OmsiMapSummary summary, OmsiMapWorkspace workspace, string label)
        {
            var count = AssetPathCount;
            if (count == null)
                throw new OmsiStepRefusedException(label + ": the path numbers of its track cannot be told without the OMSI installation's object and spline files.");
            var numbers = new Dictionary<int, Dictionary<int, int>>();
            var unknown = new Dictionary<int, string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<int>();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry == null)
                    throw new ArgumentException(label + ": entry " + Int(index) + " is missing.", nameof(entries));
                var name = label + ", entry " + Int(index) + " (element " + Int(entry.ElementId) + ", path " + Int(entry.Path) + ")";
                if (!seen.Add(entry.ToString()))
                    throw new ArgumentException(name + ": the route has this track piece twice.", nameof(entries));
                if (entry.TileIndex < 0 || entry.TileIndex >= summary.Tiles.Count || !summary.Tiles[entry.TileIndex].IsAvailable)
                    throw new ArgumentException(name + ": the map has no tile " + Int(entry.TileIndex) + ".", nameof(entries));
                var place = OmsiSignalRouteMap.ElementOnTile(summary, entry.TileIndex, entry.ElementId);
                if (place == null)
                    throw new ArgumentException(name + ": tile " + Int(entry.TileIndex) + " has no element " + Int(entry.ElementId) + ".", nameof(entries));
                if (place.Kind != OmsiMapElementKind.Object && place.Kind != OmsiMapElementKind.Spline)
                    throw new ArgumentException(name + ": element " + Int(entry.ElementId) + " is no spline or object and has no paths of its own.", nameof(entries));
                var paths = count(place.AssetPath);
                if (!paths.HasValue)
                    throw new OmsiStepRefusedException(name + ": the number of paths of " + place.AssetPath + " is unknown.");
                if (entry.Path < 0 || entry.Path >= paths.Value)
                    throw new ArgumentException(name + ": " + place.AssetPath + " has " + Int(paths.Value) + " path(s).", nameof(entries));

                if (!numbers.TryGetValue(entry.TileIndex, out var tileNumbers))
                {
                    var tile = summary.Tiles[entry.TileIndex];
                    if (!workspace.FileExists(tile.FileName))
                        throw new ArgumentException(name + ": the map has no file " + tile.FileName + ".", nameof(entries));
                    tileNumbers = OmsiTilePathNumbers.Of(OmsiRecordFile.Parse(workspace.ReadAllBytes(tile.FileName), OmsiTileRecordLayouts.Instance), count, out var unknownAsset);
                    numbers[entry.TileIndex] = tileNumbers;
                    unknown[entry.TileIndex] = unknownAsset;
                }
                if (!tileNumbers.TryGetValue(entry.ElementId, out var first))
                    throw new OmsiStepRefusedException(name + ": its path number in tile " + Int(entry.TileIndex) + " cannot be told" +
                                                       (unknown[entry.TileIndex] != null ? ": the number of paths of " + unknown[entry.TileIndex] + " is unknown." : "."));
                result.Add(first + entry.Path);
            }
            return result;
        }

        /// <summary>Each entry must be a rail and lead on to the next in the way of travel, as the lanes of the rail network join (ADR 093).</summary>
        private void CheckEntriesJoin(IList<OmsiSignalRouteDraftEntry> entries, OmsiMapSummary summary, OmsiPathNetwork rails, string label)
        {
            if (rails == null)
            {
                var installation = InstallationRoot();
                if (installation == null)
                    throw new OmsiStepRefusedException(label + ": whether its track pieces join cannot be told without the OMSI installation's object and spline files.");
                var tiles = new List<OmsiTileSummary>();
                foreach (var entry in entries)
                    if (!tiles.Contains(summary.Tiles[entry.TileIndex]))
                        tiles.Add(summary.Tiles[entry.TileIndex]);
                rails = OmsiPathNetwork.ForTiles(summary, installation, tiles, 2);
            }

            List<OmsiLane> current = null;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var tile = summary.Tiles[entry.TileIndex];
                var lanes = rails.LanesOfPath(tile.X, tile.Y, entry.ElementId, entry.Path);
                var name = label + ", entry " + Int(index) + " (element " + Int(entry.ElementId) + ", path " + Int(entry.Path) + ")";
                if (lanes.Count == 0)
                    throw new ArgumentException(name + " is no rail.", nameof(entries));
                if (current == null)
                {
                    current = new List<OmsiLane>(lanes);
                    continue;
                }
                var next = new List<OmsiLane>();
                foreach (var lane in lanes)
                    foreach (var before in current)
                        if (Contains(rails.NextOf(before), lane) && !next.Contains(lane))
                            next.Add(lane);
                if (next.Count == 0)
                    throw new ArgumentException(name + " does not join on to entry " + Int(index - 1) + " (element " + Int(entries[index - 1].ElementId) + ").",
                        nameof(entries));
                current = next;
            }
        }

        private static bool Contains(IReadOnlyList<OmsiLane> lanes, OmsiLane lane)
        {
            foreach (var each in lanes)
                if (ReferenceEquals(each, lane))
                    return true;
            return false;
        }

        /// <summary>The file with the route added at its end: "   n:", [signalroute], [signal], the entries and the optional sections with the OMSI editor's blank lines.</summary>
        private static byte[] AppendedRoute(OmsiSignalRoutes routes, int signalId, OmsiSignalRouteDraft draft, List<int> tilePaths, string label)
        {
            var file = routes.File;
            var ending = file.DefaultLineEnding;
            var numbered = true;
            var number = 0;
            foreach (var each in routes.Routes)
            {
                numbered &= each.Number == each.Index;
                number = Math.Max(number, (each.Number ?? each.Index) + 1);
            }
            if (numbered)
                number = routes.Routes.Count;

            // The line that ends the file gets its line break, then the number line follows the blank lines of the route before.
            var holder = file.Records.Count > 0 ? file.Records[file.Records.Count - 1].Trailer : file.Preamble;
            var lastLine = file.Records.Count > 0 ? file.Records[file.Records.Count - 1].LastLine : file.Preamble.Count > 0 ? file.Preamble[file.Preamble.Count - 1] : null;
            if (lastLine != null && lastLine.Ending.Length == 0)
                lastLine.Ending = ending;
            holder.Add(new OmsiLine("   " + Int(number) + ":", ending));

            var aspect = draft.Aspect ?? (draft.SpeedLimit.HasValue ? 2 : 1);
            var place = file.Records.Count;
            file.InsertRecord(place++, file.CreateRecord("signalroute", new[] { OmsiNumberFormat.Format(draft.Kind) }));
            file.InsertRecord(place++, file.CreateRecord("signal", new[] { OmsiNumberFormat.Format(signalId), OmsiNumberFormat.Format(aspect) }));
            for (var index = 0; index < draft.Entries.Count; index++)
            {
                var entry = draft.Entries[index];
                file.InsertRecord(place++, file.CreateRecord("entry", new[]
                {
                    OmsiNumberFormat.Format(entry.ElementId), OmsiNumberFormat.Format(entry.Path), OmsiNumberFormat.Format(entry.TileIndex), OmsiNumberFormat.Format(tilePaths[index])
                }));
            }

            var added = OmsiSignalRoutes.Parse(file.ToBytes());
            var route = added.Routes[added.Routes.Count - 1];
            LayOutOptionalSections(added, route, label,
                draft.DistantSignal.HasValue ? added.File.CreateRecord("distsignal", new[] { OmsiNumberFormat.Format(draft.DistantSignal.Value) }, 0) : null,
                draft.NextSignal.HasValue ? added.File.CreateRecord("nextsignal", new[] { OmsiNumberFormat.Format(draft.NextSignal.Value) }, 0) : null,
                draft.SpeedLimit.HasValue ? added.File.CreateRecord("speedlimit", new[] { SpeedText(draft.SpeedLimit.Value) }, 0) : null);
            var bytes = added.File.ToBytes();

            // The route must read back as written, and nothing before it may change.
            var check = OmsiSignalRoutes.Parse(bytes);
            if (check.Routes.Count != routes.Routes.Count + 1 || check.Problems.Count != routes.Problems.Count || !check.File.IsExactRoundTrip)
                throw new OmsiStepRefusedException(label + " does not read back from " + OmsiSignalRoutes.FileName + "; the file is left as it is.");
            var written = check.Routes[check.Routes.Count - 1];
            if (written.SignalId != signalId || written.Entries.Count != draft.Entries.Count || written.Number != number)
                throw new OmsiStepRefusedException(label + " does not read back from " + OmsiSignalRoutes.FileName + "; the file is left as it is.");
            return bytes;
        }
    }
}
