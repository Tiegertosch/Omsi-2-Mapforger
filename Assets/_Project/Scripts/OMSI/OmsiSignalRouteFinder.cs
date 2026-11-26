using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Where a signal stands and which way it faces: the way of travel of the trains it stands for (ADR 105, M4).</summary>
    public sealed class OmsiSignalPose
    {
        public OmsiSignalPose(int id, OmsiMapPoint world, double headingDegrees, int? trackElementId = null)
        {
            Id = id;
            World = world;
            HeadingDegrees = headingDegrees;
            TrackElementId = trackElementId;
        }

        public int Id { get; }

        /// <summary>The signal's place in world metres.</summary>
        public OmsiMapPoint World { get; }

        /// <summary>
        /// The heading of the signal object, 0 along +Y and clockwise. On all 49 routes of Berlin-Spandau the trains a signal stands for run this way
        /// (within 4°), for objects as for signals placed along a spline.
        /// </summary>
        public double HeadingDegrees { get; }

        /// <summary>The spline a signal placed along a spline hangs on; its lanes come first when two tracks lie as near.</summary>
        public int? TrackElementId { get; }
    }

    public enum OmsiSignalRouteTargetKind
    {
        /// <summary>Up to the given signal: the route ends with the track piece the signal stands beside, as most of OMSI's routes on Berlin-Spandau do.</summary>
        Signal,
        /// <summary>Up to and including a chosen path of a spline or object.</summary>
        Track,
        /// <summary>Up to the first signal that faces the same way, on each branch, ending as for <see cref="Signal"/>.</summary>
        NextSignal
    }

    /// <summary>Where a signal route search ends.</summary>
    public sealed class OmsiSignalRouteTarget
    {
        private OmsiSignalRouteTarget(OmsiSignalRouteTargetKind kind)
        {
            Kind = kind;
        }

        public OmsiSignalRouteTargetKind Kind { get; }

        /// <summary>The target signal's ID for <see cref="OmsiSignalRouteTargetKind.Signal"/>.</summary>
        public int? SignalId { get; private set; }

        /// <summary>The target signal's place; filled from the map when only the ID is given.</summary>
        public OmsiSignalPose SignalPose { get; private set; }

        /// <summary>The tile of the target path in the [map] list (<see cref="OmsiSignalRouteTargetKind.Track"/>), or -1 when only its coordinates are given.</summary>
        public int TileIndex { get; private set; } = -1;

        public int TileX { get; private set; }
        public int TileY { get; private set; }
        public int ElementId { get; private set; }
        public int Path { get; private set; }

        /// <summary>The signals a <see cref="OmsiSignalRouteTargetKind.NextSignal"/> search may stop at; filled from the map when null.</summary>
        public IReadOnlyList<OmsiSignalPose> Signals { get; private set; }

        public static OmsiSignalRouteTarget Signal(int signalId)
        {
            return new OmsiSignalRouteTarget(OmsiSignalRouteTargetKind.Signal) { SignalId = signalId };
        }

        public static OmsiSignalRouteTarget Signal(OmsiSignalPose pose)
        {
            if (pose == null)
                throw new ArgumentNullException(nameof(pose));
            return new OmsiSignalRouteTarget(OmsiSignalRouteTargetKind.Signal) { SignalId = pose.Id, SignalPose = pose };
        }

        /// <summary>A path of an element on a tile of the [map] list.</summary>
        public static OmsiSignalRouteTarget Track(int tileIndex, int elementId, int path)
        {
            return new OmsiSignalRouteTarget(OmsiSignalRouteTargetKind.Track) { TileIndex = tileIndex, ElementId = elementId, Path = path };
        }

        /// <summary>A path of an element on the tile at these coordinates.</summary>
        public static OmsiSignalRouteTarget TrackAt(int tileX, int tileY, int elementId, int path)
        {
            return new OmsiSignalRouteTarget(OmsiSignalRouteTargetKind.Track) { TileX = tileX, TileY = tileY, ElementId = elementId, Path = path };
        }

        /// <param name="signals">The signals to stop at; null takes every signal object of the map.</param>
        public static OmsiSignalRouteTarget NextSignal(IReadOnlyList<OmsiSignalPose> signals = null)
        {
            return new OmsiSignalRouteTarget(OmsiSignalRouteTargetKind.NextSignal) { Signals = signals };
        }

        internal OmsiSignalRouteTarget Resolved(OmsiSignalPose pose, int tileX, int tileY, IReadOnlyList<OmsiSignalPose> signals)
        {
            return new OmsiSignalRouteTarget(Kind)
            {
                SignalId = SignalId, SignalPose = pose ?? SignalPose, TileIndex = TileIndex, TileX = tileX, TileY = tileY, ElementId = ElementId, Path = Path,
                Signals = signals ?? Signals
            };
        }
    }

    /// <summary>Limits and choices of a search; the defaults suit the stock maps.</summary>
    public sealed class OmsiSignalRouteSearchOptions
    {
        /// <summary>
        /// Null starts where the signal stands as OMSI's routes do: on the track piece the signal stands beside when it stands in its first half,
        /// else on the pieces after it. True always starts on that piece, false always after it.
        /// </summary>
        public bool? StartOnSignalTrack { get; set; }

        /// <summary>How far a signal may stand beside its track, in metres.</summary>
        public double SignalReachMeters { get; set; } = 6.0;

        /// <summary>The most ways to give, shortest first.</summary>
        public int MaximumWays { get; set; } = 8;

        /// <summary>No way longer than this, in metres.</summary>
        public double MaximumLengthMeters { get; set; } = 5000.0;

        /// <summary>No way with more track pieces than this.</summary>
        public int MaximumEntries { get; set; } = 400;

        /// <summary>The search stops after looking at this many track pieces; the ways found so far are kept.</summary>
        public int MaximumSteps { get; set; } = 200000;
    }

    public enum OmsiSignalRouteSearchProblem
    {
        None,
        /// <summary>The map has no signal (object, attached object or object along a spline) with the ID.</summary>
        UnknownSignal,
        /// <summary>No rail runs beside the signal in the way it faces.</summary>
        NoTrackAtSignal,
        /// <summary>The target signal or target path is not on the map.</summary>
        UnknownTarget,
        /// <summary>The target has no rail: a path that is not a rail, or a target signal without track beside it.</summary>
        TargetHasNoTrack,
        /// <summary>No rail leads from the signal to the target.</summary>
        NoWay,
        /// <summary>No way within <see cref="OmsiSignalRouteSearchOptions.MaximumLengthMeters"/> or <see cref="OmsiSignalRouteSearchOptions.MaximumEntries"/>.</summary>
        TooFar,
        /// <summary>The search looked at <see cref="OmsiSignalRouteSearchOptions.MaximumSteps"/> pieces without a way.</summary>
        StepLimit
    }

    /// <summary>One track piece of a route: a path of a spline or object (such as one branch of a switch) on a tile of the [map] list.</summary>
    public sealed class OmsiSignalRouteDraftEntry
    {
        public OmsiSignalRouteDraftEntry(int elementId, int path, int tileIndex)
        {
            ElementId = elementId;
            Path = path;
            TileIndex = tileIndex;
        }

        public int ElementId { get; }

        /// <summary>The [path] of the element's .sco or .sli, from 0.</summary>
        public int Path { get; }

        /// <summary>The tile in the [map] list of global.cfg, from 0.</summary>
        public int TileIndex { get; }

        public override string ToString()
        {
            return ElementId.ToString(CultureInfo.InvariantCulture) + "/" + Path.ToString(CultureInfo.InvariantCulture) + "@" + TileIndex.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One way a route can take: its track pieces in the way of travel.</summary>
    public sealed class OmsiSignalRouteWay
    {
        public List<OmsiSignalRouteDraftEntry> Entries { get; } = new List<OmsiSignalRouteDraftEntry>();

        /// <summary>The lanes of the entries, in the way of travel, to draw the way.</summary>
        public List<OmsiLane> Lanes { get; } = new List<OmsiLane>();

        /// <summary>The length of all its pieces as OMSI counts them, in metres.</summary>
        public double LengthMeters { get; internal set; }

        /// <summary>The signal the way ends before (target signal or next signal), or null for a target path.</summary>
        public int? EndSignalId { get; internal set; }
    }

    /// <summary>The result of a search: the ways, shortest first, or the reason there is none.</summary>
    public sealed class OmsiSignalRouteSearch
    {
        public List<OmsiSignalRouteWay> Ways { get; } = new List<OmsiSignalRouteWay>();

        public OmsiSignalRouteSearchProblem Problem { get; internal set; }

        /// <summary>Why there is no way, in English; null when there are ways.</summary>
        public string Message { get; internal set; }

        /// <summary>The lane the signal stands beside, in its way of travel; null when there is none.</summary>
        public OmsiLane SignalTrack { get; internal set; }

        /// <summary>True when the ways start on <see cref="SignalTrack"/>, false when they start on the pieces after it.</summary>
        public bool StartsOnSignalTrack { get; internal set; }

        /// <summary>True when there may be more ways than <see cref="OmsiSignalRouteSearchOptions.MaximumWays"/> or the step limit let the search find.</summary>
        public bool MoreWaysPossible { get; internal set; }

        public bool Found => Ways.Count > 0;
    }

    /// <summary>
    /// Finds the ways of a signal route over the rails (ADR 105, M4): from the track at a signal, in the way it faces, along the lanes of the rail network
    /// (<see cref="OmsiPathNetwork"/> of kind 2, a switch giving one lane for each of its paths) to a target signal, a chosen path or the next signal. It gives
    /// several ways where switches branch, shortest first, each piece at most once.
    /// </summary>
    public static class OmsiSignalRouteFinder
    {
        private const double HeadingTolerance = 60.0;

        /// <summary>A signal on the left of its track counts as standing this much further off than one on the right.</summary>
        private const double LeftSidePenalty = 2.0;

        /// <summary>
        /// The pose of a signal of the map: an object, an attached object (at its parent, turned with it) or an object along a spline (at its place along
        /// the spline, turned with it). Null when the map has no such element or it is a spline.
        /// </summary>
        public static OmsiSignalPose PoseOf(OmsiMapSummary summary, int id)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            for (var index = 0; index < summary.Tiles.Count; index++)
            {
                var pose = PoseOnTile(summary, index, id);
                if (pose != null)
                    return pose;
            }
            return null;
        }

        /// <summary>Every object, attached object and object along a spline whose file is a signal (<paramref name="isSignalAsset"/> true).</summary>
        public static List<OmsiSignalPose> SignalPoses(OmsiMapSummary summary, Func<string, bool?> isSignalAsset)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (isSignalAsset == null)
                throw new ArgumentNullException(nameof(isSignalAsset));
            var result = new List<OmsiSignalPose>();
            var known = new Dictionary<string, bool>(StringComparer.Ordinal);
            Func<string, bool> isSignal = asset =>
            {
                if (string.IsNullOrEmpty(asset))
                    return false;
                if (!known.TryGetValue(asset, out var value))
                    known[asset] = value = isSignalAsset(asset) == true;
                return value;
            };
            for (var index = 0; index < summary.Tiles.Count; index++)
            {
                var tile = summary.Tiles[index];
                foreach (var item in tile.Objects)
                    if (isSignal(item.AssetPath))
                        result.Add(ObjectPose(summary, tile, item));
                foreach (var item in tile.AttachedObjects)
                    if (isSignal(item.AssetPath))
                    {
                        var pose = AttachedPose(summary, tile, item);
                        if (pose != null)
                            result.Add(pose);
                    }
                foreach (var item in tile.SplineAttachments)
                    if (isSignal(item.AssetPath))
                    {
                        var pose = AlongSplinePose(summary, tile, item);
                        if (pose != null)
                            result.Add(pose);
                    }
            }
            return result;
        }

        /// <summary>
        /// Finds the ways from a signal of the map to a target. A target signal or path given by ID is looked up in <paramref name="summary"/>; a next-signal
        /// search without its own list stops at every object of the map whose file is a signal (<paramref name="isSignalAsset"/>).
        /// </summary>
        /// <param name="rails">The rails of the map: <c>OmsiPathNetwork.ForMap(summary, installation, 2)</c>.</param>
        public static OmsiSignalRouteSearch Find(OmsiMapSummary summary, OmsiPathNetwork rails, int signalId, OmsiSignalRouteTarget target,
            Func<string, bool?> isSignalAsset = null, OmsiSignalRouteSearchOptions options = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (rails == null)
                throw new ArgumentNullException(nameof(rails));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            var pose = PoseOf(summary, signalId);
            if (pose == null)
                return Failed(OmsiSignalRouteSearchProblem.UnknownSignal, "The map has no signal " + Int(signalId) + " (an object or an object along a spline).");

            OmsiSignalPose targetPose = null;
            var tileX = target.TileX;
            var tileY = target.TileY;
            IReadOnlyList<OmsiSignalPose> signals = null;
            switch (target.Kind)
            {
                case OmsiSignalRouteTargetKind.Signal:
                    targetPose = target.SignalPose ?? (target.SignalId.HasValue ? PoseOf(summary, target.SignalId.Value) : null);
                    if (targetPose == null)
                        return Failed(OmsiSignalRouteSearchProblem.UnknownTarget, "The map has no signal " + Int(target.SignalId ?? -1) + " to end the route at.");
                    break;
                case OmsiSignalRouteTargetKind.Track:
                    if (target.TileIndex >= 0)
                    {
                        if (target.TileIndex >= summary.Tiles.Count)
                            return Failed(OmsiSignalRouteSearchProblem.UnknownTarget, "The map has no tile " + Int(target.TileIndex) + ".");
                        tileX = summary.Tiles[target.TileIndex].X;
                        tileY = summary.Tiles[target.TileIndex].Y;
                    }
                    break;
                case OmsiSignalRouteTargetKind.NextSignal:
                    if (target.Signals == null)
                    {
                        if (isSignalAsset == null)
                            throw new ArgumentException("A search for the next signal needs the signals or a way to tell signal files.", nameof(isSignalAsset));
                        signals = SignalPoses(summary, isSignalAsset);
                    }
                    break;
            }
            var tiles = new Dictionary<long, int>();
            for (var index = 0; index < summary.Tiles.Count; index++)
            {
                var key = TileKey(summary.Tiles[index].X, summary.Tiles[index].Y);
                if (!tiles.ContainsKey(key))
                    tiles.Add(key, index);
            }
            return Find(rails, pose, target.Resolved(targetPose, tileX, tileY, signals), (x, y) => tiles.TryGetValue(TileKey(x, y), out var found) ? found : -1, options);
        }

        /// <summary>Finds the ways from a signal at a known place; every target must carry what it needs (pose, tile coordinates, signals).</summary>
        /// <param name="tileIndexOf">The index of a tile in the [map] list for its X and Y.</param>
        public static OmsiSignalRouteSearch Find(OmsiPathNetwork rails, OmsiSignalPose signal, OmsiSignalRouteTarget target, Func<int, int, int> tileIndexOf,
            OmsiSignalRouteSearchOptions options = null)
        {
            if (rails == null)
                throw new ArgumentNullException(nameof(rails));
            if (signal == null)
                throw new ArgumentNullException(nameof(signal));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (tileIndexOf == null)
                throw new ArgumentNullException(nameof(tileIndexOf));
            options = options ?? new OmsiSignalRouteSearchOptions();
            var search = new OmsiSignalRouteSearch();
            var starts = StartLanes(rails, signal, options, out var carrier, out var onCarrier);
            search.SignalTrack = carrier;
            search.StartsOnSignalTrack = onCarrier;
            if (carrier == null)
                return Failed(search, OmsiSignalRouteSearchProblem.NoTrackAtSignal,
                    "No rail runs within " + Int((int)Math.Round(options.SignalReachMeters)) + " m of signal " + Int(signal.Id) + " in the way it faces.");
            if (starts.Count == 0)
                return Failed(search, OmsiSignalRouteSearchProblem.NoWay, "The rail at signal " + Int(signal.Id) + " leads nowhere after the signal.");

            // The goal lanes a way ends with: the target path, or the piece a signal stands beside (Berlin-Spandau: OMSI's routes end on it).
            var goals = new Dictionary<int, int?>();
            switch (target.Kind)
            {
                case OmsiSignalRouteTargetKind.Track:
                    var found = false;
                    foreach (var lane in rails.LanesOfPath(target.TileX, target.TileY, target.ElementId, target.Path))
                    {
                        goals[lane.Index] = null;
                        found = true;
                    }
                    if (!found)
                        return Failed(search, OmsiSignalRouteSearchProblem.TargetHasNoTrack,
                            "Path " + Int(target.Path) + " of element " + Int(target.ElementId) + " is no rail of the map.");
                    break;
                case OmsiSignalRouteTargetKind.Signal:
                    if (target.SignalPose == null)
                        throw new ArgumentException("A target signal needs its place.", nameof(target));
                    var targetTrack = SignalTrack(rails, target.SignalPose, options.SignalReachMeters, out _, out _);
                    if (targetTrack == null)
                        return Failed(search, OmsiSignalRouteSearchProblem.TargetHasNoTrack,
                            "No rail runs within " + Int((int)Math.Round(options.SignalReachMeters)) + " m of signal " + Int(target.SignalPose.Id) + " in the way it faces.");
                    goals[targetTrack.Index] = target.SignalPose.Id;
                    break;
                case OmsiSignalRouteTargetKind.NextSignal:
                    if (target.Signals == null)
                        throw new ArgumentException("A search for the next signal needs the signals.", nameof(target));
                    SignalTrack(rails, signal, options.SignalReachMeters, out var ownAlong, out _);
                    foreach (var other in target.Signals)
                    {
                        if (other == null || other.Id == signal.Id)
                            continue;
                        var track = SignalTrack(rails, other, options.SignalReachMeters, out var along, out _);
                        // A signal beside the own piece counts only ahead of the own signal; one at the same place (a distant signal on the same post) does not.
                        if (track == null || (track == carrier && along <= ownAlong + 1.0) || goals.ContainsKey(track.Index))
                            continue;
                        goals[track.Index] = other.Id;
                    }
                    break;
            }

            Search(rails, starts, goals, options, tileIndexOf, search);
            return search;
        }

        /// <summary>
        /// The lanes a route of a signal starts with: the lane beside the signal in its way of travel (the nearest within reach, a lane of the spline the signal
        /// hangs on first, one with the signal on its right before one with it on its left), or the lanes after it when the signal stands in its second half.
        /// </summary>
        public static List<OmsiLane> StartLanes(OmsiPathNetwork rails, OmsiSignalPose signal, OmsiSignalRouteSearchOptions options, out OmsiLane signalTrack,
            out bool startsOnSignalTrack)
        {
            if (rails == null)
                throw new ArgumentNullException(nameof(rails));
            if (signal == null)
                throw new ArgumentNullException(nameof(signal));
            options = options ?? new OmsiSignalRouteSearchOptions();
            startsOnSignalTrack = false;
            signalTrack = SignalTrack(rails, signal, options.SignalReachMeters, out var bestAlong, out var bestLength);
            var result = new List<OmsiLane>();
            if (signalTrack == null)
                return result;
            startsOnSignalTrack = options.StartOnSignalTrack ?? bestAlong <= bestLength / 2.0;
            if (startsOnSignalTrack)
                result.Add(signalTrack);
            else
                result.AddRange(rails.NextOf(signalTrack));
            return result;
        }

        /// <summary>
        /// The lane a signal stands beside in its way of travel: the nearest within reach heading its way, a lane of the spline the signal hangs on first,
        /// one with the signal on its right before one with it on its left. Null when there is none.
        /// </summary>
        public static OmsiLane SignalTrack(OmsiPathNetwork rails, OmsiSignalPose signal, double reachMeters, out double alongMeters, out double laneMeters)
        {
            if (rails == null)
                throw new ArgumentNullException(nameof(rails));
            if (signal == null)
                throw new ArgumentNullException(nameof(signal));
            OmsiLane best = null;
            alongMeters = 0.0;
            laneMeters = 0.0;
            var bestScore = double.MaxValue;
            foreach (var lane in rails.LanesAround(signal.World, reachMeters))
            {
                Project(lane.Geometry, signal.World, out var along, out var distance, out var heading, out var right, out var length);
                if (distance > reachMeters || Math.Abs(OmsiRoadPlanner.Normalize(heading - signal.HeadingDegrees)) > HeadingTolerance)
                    continue;
                var score = distance + (right ? 0.0 : LeftSidePenalty);
                if (signal.TrackElementId.HasValue && lane.Path.OnSpline && lane.Path.ElementId == signal.TrackElementId.Value)
                    score -= 1000.0;
                if (score < bestScore - 1e-9 || (Math.Abs(score - bestScore) <= 1e-9 && best != null && lane.Index < best.Index))
                {
                    bestScore = score;
                    best = lane;
                    alongMeters = along;
                    laneMeters = length;
                }
            }
            return best;
        }

        private sealed class Step
        {
            public int Lane;
            public Step Before;
            public double Cost;
            public int Depth;
            public long Order;
        }

        /// <summary>A lower path of an element wins between ways of the same length (Berlin-Spandau: OMSI's routes take path 0 of a switch before path 2).</summary>
        private const double PathTieBreak = 1e-4;

        private static void Search(OmsiPathNetwork rails, List<OmsiLane> starts, Dictionary<int, int?> goals, OmsiSignalRouteSearchOptions options,
            Func<int, int, int> tileIndexOf, OmsiSignalRouteSearch search)
        {
            var lanes = rails.Lanes;
            var visits = new Dictionary<int, int>();
            var queue = new SortedSet<Step>(Comparer<Step>.Create((first, second) =>
            {
                var order = first.Cost.CompareTo(second.Cost);
                return order != 0 ? order : first.Order.CompareTo(second.Order);
            }));
            long counter = 0;
            foreach (var lane in starts)
                queue.Add(new Step { Lane = lane.Index, Cost = lane.LengthMeters + lane.Path.PathIndex * PathTieBreak, Depth = 1, Order = counter++ });
            var ways = new List<OmsiSignalRouteWay>();
            var steps = 0;
            var cutByLength = false;
            var stoppedBySteps = false;
            var maximumWays = Math.Max(1, options.MaximumWays);
            // The k shortest ways: a piece is taken at most once per way and looked at most once per way wanted.
            while (queue.Count > 0)
            {
                var step = queue.Min;
                queue.Remove(step);
                if (++steps > options.MaximumSteps)
                {
                    stoppedBySteps = true;
                    break;
                }
                visits.TryGetValue(step.Lane, out var seen);
                if (seen >= maximumWays)
                    continue;
                visits[step.Lane] = seen + 1;

                if (goals.TryGetValue(step.Lane, out var endSignal))
                {
                    ways.Add(Way(step, lanes, tileIndexOf, endSignal));
                    if (ways.Count >= maximumWays)
                    {
                        search.MoreWaysPossible = queue.Count > 0;
                        break;
                    }
                    continue;
                }

                foreach (var next in lanes[step.Lane].Next)
                {
                    var lane = lanes[next];
                    if (Contains(step, lane.Path, lanes))
                        continue;
                    var cost = step.Cost + lane.LengthMeters + lane.Path.PathIndex * PathTieBreak;
                    if (cost > options.MaximumLengthMeters || step.Depth + 1 > options.MaximumEntries)
                    {
                        cutByLength = true;
                        continue;
                    }
                    queue.Add(new Step { Lane = next, Before = step, Cost = cost, Depth = step.Depth + 1, Order = counter++ });
                }
            }
            if (stoppedBySteps)
                search.MoreWaysPossible = true;
            search.Ways.AddRange(ways);
            if (ways.Count > 0)
                return;
            if (stoppedBySteps)
            {
                search.Problem = OmsiSignalRouteSearchProblem.StepLimit;
                search.Message = "The search stopped after " + Int(options.MaximumSteps) + " track pieces without reaching the target.";
            }
            else if (cutByLength)
            {
                search.Problem = OmsiSignalRouteSearchProblem.TooFar;
                search.Message = "No rail leads to the target within " + Int((int)Math.Round(options.MaximumLengthMeters)) + " m and " + Int(options.MaximumEntries) + " track pieces.";
            }
            else
            {
                search.Problem = OmsiSignalRouteSearchProblem.NoWay;
                search.Message = "No rail leads from the signal to the target in the way the signal faces.";
            }
        }

        private static bool Contains(Step step, OmsiMapPath path, IReadOnlyList<OmsiLane> lanes)
        {
            for (var each = step; each != null; each = each.Before)
                if (ReferenceEquals(lanes[each.Lane].Path, path))
                    return true;
            return false;
        }

        private static OmsiSignalRouteWay Way(Step last, IReadOnlyList<OmsiLane> lanes, Func<int, int, int> tileIndexOf, int? endSignal)
        {
            var chain = new List<OmsiLane>();
            for (var each = last; each != null; each = each.Before)
                chain.Add(lanes[each.Lane]);
            chain.Reverse();
            var way = new OmsiSignalRouteWay { EndSignalId = endSignal };
            foreach (var lane in chain)
            {
                way.Lanes.Add(lane);
                way.Entries.Add(new OmsiSignalRouteDraftEntry(lane.Path.ElementId, lane.Path.PathIndex, tileIndexOf(lane.Path.TileX, lane.Path.TileY)));
                way.LengthMeters += lane.LengthMeters;
            }
            return way;
        }

        /// <summary>The nearest point of a line to a position: how far along it, how far off, the heading there and whether the position is on the right.</summary>
        private static void Project(OmsiMapPoint[] points, OmsiMapPoint position, out double along, out double distance, out double heading, out bool right,
            out double length)
        {
            distance = double.MaxValue;
            along = 0.0;
            heading = 0.0;
            right = true;
            length = 0.0;
            for (var index = 1; index < points.Length; index++)
            {
                var a = points[index - 1];
                var b = points[index];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var squared = dx * dx + dy * dy;
                var segment = Math.Sqrt(squared);
                var t = squared > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((position.X - a.X) * dx + (position.Y - a.Y) * dy) / squared)) : 0.0;
                var px = a.X + dx * t;
                var py = a.Y + dy * t;
                var off = Math.Sqrt((position.X - px) * (position.X - px) + (position.Y - py) * (position.Y - py));
                if (off < distance && squared > 0.0)
                {
                    distance = off;
                    along = length + segment * t;
                    heading = Math.Atan2(dx, dy) * 180.0 / Math.PI;
                    right = dx * (position.Y - a.Y) - dy * (position.X - a.X) <= 0.0;
                }
                length += segment;
            }
        }

        private static OmsiSignalPose PoseOnTile(OmsiMapSummary summary, int tileIndex, int id)
        {
            var tile = summary.Tiles[tileIndex];
            foreach (var item in tile.Objects)
                if (item.Id == id)
                    return ObjectPose(summary, tile, item);
            foreach (var item in tile.AttachedObjects)
                if (item.Id == id)
                    return AttachedPose(summary, tile, item);
            foreach (var item in tile.SplineAttachments)
                if (item.Id == id)
                    return AlongSplinePose(summary, tile, item);
            return null;
        }

        private static OmsiSignalPose ObjectPose(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord item)
        {
            return new OmsiSignalPose(item.Id, OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, item.LocalX, item.LocalY, summary.TileSizeMeters), item.HeadingDegrees);
        }

        /// <summary>An attached object stands at its parent (the attachment point needs the parent's .sco) and turns with it.</summary>
        private static OmsiSignalPose AttachedPose(OmsiMapSummary summary, OmsiTileSummary tile, OmsiAttachedObjectRecord item)
        {
            var heading = item.HeadingDegrees;
            var parentId = item.ParentId;
            for (var guard = 0; guard < 16; guard++)
            {
                foreach (var parent in tile.Objects)
                    if (parent.Id == parentId)
                        return new OmsiSignalPose(item.Id, OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, parent.LocalX, parent.LocalY, summary.TileSizeMeters),
                            heading + parent.HeadingDegrees);
                var found = false;
                foreach (var attached in tile.AttachedObjects)
                    if (attached.Id == parentId)
                    {
                        heading += attached.HeadingDegrees;
                        parentId = attached.ParentId;
                        found = true;
                        break;
                    }
                if (!found)
                    return null;
            }
            return null;
        }

        private static OmsiSignalPose AlongSplinePose(OmsiMapSummary summary, OmsiTileSummary tile, OmsiSplineAttachmentRecord item)
        {
            if (item.SplineIndex < 0 || item.SplineIndex >= tile.Splines.Count)
                return null;
            var spline = tile.Splines[item.SplineIndex];
            OmsiMapGeometry.GetLocalFrameAt(spline, item.StartDistance, out var point, out var heading);
            // Heading 0 runs along +Y and grows clockwise, so the right is (cos, -sin).
            var world = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, point.X + Math.Cos(heading) * item.LateralOffset, point.Y - Math.Sin(heading) * item.LateralOffset,
                summary.TileSizeMeters);
            return new OmsiSignalPose(item.Id, world, heading * 180.0 / Math.PI + item.HeadingDegrees, spline.Id);
        }

        private static OmsiSignalRouteSearch Failed(OmsiSignalRouteSearchProblem problem, string message)
        {
            return Failed(new OmsiSignalRouteSearch(), problem, message);
        }

        private static OmsiSignalRouteSearch Failed(OmsiSignalRouteSearch search, OmsiSignalRouteSearchProblem problem, string message)
        {
            search.Problem = problem;
            search.Message = message;
            return search;
        }

        private static long TileKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
