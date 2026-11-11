using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A trip of a train or tram to write: its track and the trip file on it.</summary>
    public sealed class OmsiRailTripSpec
    {
        /// <summary>The name of the trip file and of its track file, without extension.</summary>
        public string Name { get; set; }

        public string Destination { get; set; }
        public OmsiTrackPlan Plan { get; set; }
        public double RunningMinutes { get; set; }
        public string ProfileName { get; set; } = "standard";
    }

    /// <summary>Tracks and trips on rails (ADR 097).</summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>The way over the rails for stations and waypoints in order; <paramref name="rails"/> is <c>OmsiPathNetwork.ForMap(summary, root, 2)</c>.</summary>
        public OmsiTrackPlan PlanTrack(OmsiMapSummary summary, IList<OmsiTripStopChoice> stops, OmsiPathNetwork rails)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            return OmsiTrackPlanner.Plan(stops, rails, (x, y) => summary.Tiles.FindIndex(tile => tile.X == x && tile.Y == y));
        }

        /// <summary>How many paths the object or spline file of an asset has, read once for each from the installation.</summary>
        private sealed class AssetPathCounter
        {
            private readonly string installation;
            private readonly OmsiSplineProfileCatalog profiles;
            private readonly Dictionary<string, int?> counts = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

            public AssetPathCounter(string installation)
            {
                this.installation = installation;
                profiles = new OmsiSplineProfileCatalog(installation);
            }

            public int? Count(string asset)
            {
                if (string.IsNullOrWhiteSpace(asset))
                    return null;
                if (counts.TryGetValue(asset, out var known))
                    return known;
                int? count = null;
                try
                {
                    if (asset.EndsWith(".sli", StringComparison.OrdinalIgnoreCase))
                    {
                        count = profiles.Get(asset)?.Lanes.Count;
                    }
                    else
                    {
                        var path = OmsiRelativePath.Resolve(installation, asset);
                        if (File.Exists(path))
                            count = OmsiSceneryObject.Parse(File.ReadAllBytes(path)).Paths.Count;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException || exception is ArgumentException)
                {
                    // An unreadable file counts no paths.
                }
                counts[asset] = count;
                return count;
            }
        }

        /// <summary>The number OMSI keeps for each entry's path in its tile: where the element's first path stands in the tile's list, plus the path's place in the element.</summary>
        private List<int> TileNumbersOf(OmsiMapSummary summary, IList<OmsiPathEntry> entries)
        {
            var workspace = RequireWorkspace();
            var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(Path.GetDirectoryName(Path.GetFullPath(GlobalConfigPath)));
            if (string.IsNullOrEmpty(installation))
                throw new InvalidOperationException("The OMSI installation is needed to number the paths of a track.");
            var counter = new AssetPathCounter(installation);
            var tiles = new Dictionary<int, Dictionary<int, int>>();
            var result = new List<int>();
            foreach (var entry in entries)
            {
                if (entry.TileIndex < 0 || entry.TileIndex >= summary.Tiles.Count)
                    throw new InvalidOperationException("An entry lies on a tile the map does not have.");
                if (!tiles.TryGetValue(entry.TileIndex, out var numbers))
                {
                    var file = OmsiRecordFile.Parse(workspace.ReadAllBytes(summary.Tiles[entry.TileIndex].FileName), OmsiTileRecordLayouts.Instance);
                    tiles[entry.TileIndex] = numbers = OmsiTilePathNumbers.Of(file, counter.Count);
                }
                if (!numbers.TryGetValue(entry.ElementId, out var first))
                    throw new InvalidOperationException("The element " + entry.ElementId + " is not in the file of tile " + summary.Tiles[entry.TileIndex].FileName + ".");
                result.Add(first);
            }
            return result;
        }

        /// <summary>
        /// Writes the trips of a line on rails: for each trip its track file and its trip file, and the line file when the map has no line of that name. Everything
        /// is computed first and nothing is written when a plan has problems or a file exists. One step that undoes all of it; null when there is nothing to write.
        /// </summary>
        public OmsiTerrainChange CreateRailLine(OmsiMapSummary summary, string lineName, string vehicleGroup, IList<OmsiRailTripSpec> trips)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (trips == null)
                throw new ArgumentNullException(nameof(trips));
            var workspace = RequireWorkspace();
            var files = new List<KeyValuePair<string, byte[]>>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var trip in trips)
            {
                if (trip.Plan == null || !trip.Plan.IsValid)
                    throw new InvalidOperationException("The trip " + trip.Name + " has problems: " + string.Join(" ", trip.Plan?.Problems ?? new List<string>()));
                var name = TimetableName(trip.Name, "A trip");
                var tripPath = TimetablePath(name, ".ttp");
                var trackPath = TimetablePath(name, ".ttr");
                if (!names.Add(name) || workspace.FileExists(tripPath) || workspace.FileExists(trackPath))
                    throw new InvalidOperationException("The map has a trip or track named " + name + " already.");
                files.Add(new KeyValuePair<string, byte[]>(trackPath, OmsiTrackWriter.CreateTrack(trip.Plan.Entries, TileNumbersOf(summary, trip.Plan.Entries))));
                files.Add(new KeyValuePair<string, byte[]>(tripPath, OmsiTrackWriter.CreateTrip(name, trip.Destination, lineName, trip.Plan.Stations, trip.ProfileName, trip.RunningMinutes)));
            }
            var linePath = TimetablePath(lineName, ".ttl");
            if (!workspace.FileExists(linePath))
                files.Add(new KeyValuePair<string, byte[]>(linePath, OmsiTimetableWriter.CreateLine(vehicleGroup)));
            return TimetableFilesChange(files);
        }
    }
}
