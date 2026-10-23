using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A stop of the timetable where the map has its object, in world metres.</summary>
    public sealed class OmsiRouteStop
    {
        public string Name { get; internal set; }
        public int ObjectId { get; internal set; }
        public OmsiMapPoint Position { get; internal set; }
    }

    /// <summary>A line of the timetable with the lanes its trips drive and the stops they serve.</summary>
    public sealed class OmsiRouteLine
    {
        public string Name { get; internal set; }

        /// <summary>One polyline in world metres per driven lane of a spline or object, each lane once.</summary>
        public List<OmsiMapPoint[]> Paths { get; } = new List<OmsiMapPoint[]>();

        /// <summary>Object IDs of the stops its trips serve.</summary>
        public HashSet<int> StopIds { get; } = new HashSet<int>();

        /// <summary>Trips the line runs over the day, counted over all tours.</summary>
        public int ScheduledTripCount { get; internal set; }
    }

    /// <summary>
    /// Where the timetable runs on the map: stops at their objects and, per line, the lanes of the splines and objects
    /// its trips drive. A trip with its own track (.ttr) drives the track's entries; any other trip drives the station
    /// links between its stops. Spline lanes lie beside the spline by the [path] offset of their .sli, object lanes
    /// follow the [path] of the .sco. Positions are world metres like <see cref="OmsiMapGeometry.TileLocalToWorld"/>.
    /// </summary>
    public sealed class OmsiTimetableRoutes
    {
        public List<OmsiRouteStop> Stops { get; } = new List<OmsiRouteStop>();
        public List<OmsiRouteLine> Lines { get; } = new List<OmsiRouteLine>();

        /// <summary>Entries whose element, lane or file the map or the installation does not have; they are left out.</summary>
        public int MissingEntryCount { get; private set; }

        /// <summary>Builds the routes with the object and spline files of the installation at <paramref name="installationRoot"/>.</summary>
        public static OmsiTimetableRoutes Build(OmsiTimetable timetable, OmsiMapSummary summary, string installationRoot)
        {
            if (string.IsNullOrEmpty(installationRoot))
                throw new ArgumentException("The OMSI installation is needed for the lanes of objects and splines.", nameof(installationRoot));

            var profiles = new OmsiSplineProfileCatalog(installationRoot);
            var objects = new Dictionary<string, OmsiSceneryObject>(StringComparer.OrdinalIgnoreCase);
            return Build(timetable, summary, assetPath =>
            {
                if (objects.TryGetValue(assetPath, out var cached))
                    return cached;
                OmsiSceneryObject sceneryObject = null;
                try
                {
                    var path = OmsiRelativePath.Resolve(installationRoot, assetPath);
                    if (File.Exists(path))
                        sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(path));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException ||
                                                  exception is ArgumentException)
                {
                    // An unreadable object has no lanes; its entries count as missing.
                }
                objects.Add(assetPath, sceneryObject);
                return sceneryObject;
            }, profiles.Get);
        }

        /// <param name="sceneryObjects">The .sco of an asset path, or null when there is none.</param>
        /// <param name="splineProfiles">The .sli of an asset path, or null when there is none.</param>
        public static OmsiTimetableRoutes Build(OmsiTimetable timetable, OmsiMapSummary summary, Func<string, OmsiSceneryObject> sceneryObjects,
            Func<string, OmsiSplineProfile> splineProfiles)
        {
            if (timetable == null)
                throw new ArgumentNullException(nameof(timetable));
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (sceneryObjects == null)
                throw new ArgumentNullException(nameof(sceneryObjects));
            if (splineProfiles == null)
                throw new ArgumentNullException(nameof(splineProfiles));

            var result = new OmsiTimetableRoutes();
            var builder = new Builder(summary, sceneryObjects, splineProfiles);

            var stopIds = new HashSet<int>();
            foreach (var stop in timetable.BusStops)
                if (stopIds.Add(stop.ObjectId) && builder.TryObject(stop.TileIndex, stop.ObjectId, out var tile, out var record))
                    result.Stops.Add(new OmsiRouteStop
                    {
                        Name = stop.Name, ObjectId = stop.ObjectId,
                        Position = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, record.LocalX, record.LocalY, summary.TileSizeMeters)
                    });

            var links = new Dictionary<long, OmsiStationLink>();
            foreach (var link in timetable.StationLinks)
            {
                var key = LinkKey(link.FromStopId, link.ToStopId);
                if (!links.ContainsKey(key))
                    links.Add(key, link);
            }

            foreach (var line in timetable.Lines)
            {
                var route = new OmsiRouteLine { Name = line.Name };
                var lanes = new HashSet<string>(StringComparer.Ordinal);
                var trips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tour in line.Tours)
                    foreach (var tourTrip in tour.Trips)
                    {
                        route.ScheduledTripCount++;
                        if (!trips.Add(tourTrip.Trip) || !timetable.Trips.TryGetValue(tourTrip.Trip, out var trip))
                            continue;
                        foreach (var stop in trip.Stops)
                            route.StopIds.Add(stop.ObjectId);
                        foreach (var entry in DrivenEntries(timetable, trip, links))
                            if (lanes.Add(entry.TileIndex + "/" + entry.ElementId + "/" + entry.PathIndex))
                            {
                                var points = builder.Lane(entry);
                                if (points != null)
                                    route.Paths.Add(points);
                            }
                    }
                result.Lines.Add(route);
            }

            result.MissingEntryCount = builder.MissingEntryCount;
            return result;
        }

        private static IEnumerable<OmsiPathEntry> DrivenEntries(OmsiTimetable timetable, OmsiTrip trip, Dictionary<long, OmsiStationLink> links)
        {
            if (!string.IsNullOrEmpty(trip.Track))
            {
                if (timetable.Tracks.TryGetValue(trip.Track, out var track))
                    foreach (var entry in track.Entries)
                        yield return entry;
                yield break;
            }

            for (var index = 0; index + 1 < trip.Stops.Count; index++)
                if (links.TryGetValue(LinkKey(trip.Stops[index].ObjectId, trip.Stops[index + 1].ObjectId), out var link))
                    foreach (var entry in link.Entries)
                        yield return entry;
        }

        private static long LinkKey(int from, int to)
        {
            return ((long)from << 32) | (uint)to;
        }

        /// <summary>Finds elements by tile index and ID and samples their lanes, each lane once.</summary>
        private sealed class Builder
        {
            private readonly OmsiMapSummary summary;
            private readonly Func<string, OmsiSceneryObject> sceneryObjects;
            private readonly Func<string, OmsiSplineProfile> splineProfiles;
            private readonly Dictionary<int, Dictionary<int, OmsiObjectRecord>> objectsByTile = new Dictionary<int, Dictionary<int, OmsiObjectRecord>>();
            private readonly Dictionary<int, Dictionary<int, OmsiSplineRecord>> splinesByTile = new Dictionary<int, Dictionary<int, OmsiSplineRecord>>();
            private readonly Dictionary<string, OmsiMapPoint[]> lanes = new Dictionary<string, OmsiMapPoint[]>(StringComparer.Ordinal);

            public Builder(OmsiMapSummary summary, Func<string, OmsiSceneryObject> sceneryObjects, Func<string, OmsiSplineProfile> splineProfiles)
            {
                this.summary = summary;
                this.sceneryObjects = sceneryObjects;
                this.splineProfiles = splineProfiles;
            }

            public int MissingEntryCount { get; private set; }

            public bool TryObject(int tileIndex, int id, out OmsiTileSummary tile, out OmsiObjectRecord record)
            {
                tile = null;
                record = null;
                if (tileIndex < 0 || tileIndex >= summary.Tiles.Count)
                    return false;
                tile = summary.Tiles[tileIndex];
                if (!objectsByTile.TryGetValue(tileIndex, out var objects))
                {
                    objects = new Dictionary<int, OmsiObjectRecord>();
                    foreach (var item in tile.Objects)
                        objects[item.Id] = item;
                    objectsByTile.Add(tileIndex, objects);
                }
                return objects.TryGetValue(id, out record);
            }

            private bool TrySpline(int tileIndex, int id, out OmsiTileSummary tile, out OmsiSplineRecord record)
            {
                tile = null;
                record = null;
                if (tileIndex < 0 || tileIndex >= summary.Tiles.Count)
                    return false;
                tile = summary.Tiles[tileIndex];
                if (!splinesByTile.TryGetValue(tileIndex, out var splines))
                {
                    splines = new Dictionary<int, OmsiSplineRecord>();
                    foreach (var item in tile.Splines)
                        splines[item.Id] = item;
                    splinesByTile.Add(tileIndex, splines);
                }
                return splines.TryGetValue(id, out record);
            }

            /// <summary>The lane as a polyline in world metres, or null when the map or installation lacks it.</summary>
            public OmsiMapPoint[] Lane(OmsiPathEntry entry)
            {
                var key = entry.TileIndex + "/" + entry.ElementId + "/" + entry.PathIndex;
                if (lanes.TryGetValue(key, out var cached))
                    return cached;
                var points = SplineLane(entry) ?? ObjectLane(entry);
                if (points == null)
                    MissingEntryCount++;
                lanes.Add(key, points);
                return points;
            }

            private OmsiMapPoint[] SplineLane(OmsiPathEntry entry)
            {
                if (!TrySpline(entry.TileIndex, entry.ElementId, out var tile, out var spline))
                    return null;
                var profile = splineProfiles(spline.AssetPath);
                if (profile == null || entry.PathIndex < 0 || entry.PathIndex >= profile.Lanes.Count || !(spline.LengthMeters > 0.0) ||
                    double.IsInfinity(spline.LengthMeters))
                    return null;
                var offset = profile.Lanes[entry.PathIndex].X * (spline.IsMirrored ? -1.0 : 1.0);
                var count = SegmentCount(spline.LengthMeters);
                var points = new OmsiMapPoint[count + 1];
                for (var index = 0; index <= count; index++)
                {
                    OmsiMapGeometry.GetLocalFrameAt(spline, spline.LengthMeters * index / count, out var position, out var heading);
                    points[index] = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, position.X + Math.Cos(heading) * offset,
                        position.Y - Math.Sin(heading) * offset, summary.TileSizeMeters);
                }
                return points;
            }

            private OmsiMapPoint[] ObjectLane(OmsiPathEntry entry)
            {
                if (!TryObject(entry.TileIndex, entry.ElementId, out var tile, out var record))
                    return null;
                var sceneryObject = sceneryObjects(record.AssetPath);
                if (sceneryObject == null || entry.PathIndex < 0 || entry.PathIndex >= sceneryObject.Paths.Count)
                    return null;
                var path = sceneryObject.Paths[entry.PathIndex];
                if (!(path.LengthMeters > 0.0) || double.IsInfinity(path.LengthMeters))
                    return null;

                // The lane in the object frame (x right, y forward) like a spline from its start, then turned by the heading.
                var lane = new OmsiSplineRecord
                {
                    LocalX = path.X, LocalY = path.Y, HeadingDegrees = path.HeadingDegrees, LengthMeters = path.LengthMeters,
                    RadiusMeters = path.RadiusMeters
                };
                var heading = record.HeadingDegrees * Math.PI / 180.0;
                var cos = Math.Cos(heading);
                var sin = Math.Sin(heading);
                var count = SegmentCount(path.LengthMeters);
                var points = new OmsiMapPoint[count + 1];
                for (var index = 0; index <= count; index++)
                {
                    OmsiMapGeometry.GetLocalFrameAt(lane, path.LengthMeters * index / count, out var local, out _);
                    points[index] = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, record.LocalX + local.X * cos + local.Y * sin,
                        record.LocalY - local.X * sin + local.Y * cos, summary.TileSizeMeters);
                }
                return points;
            }

            /// <summary>A point every 4 m, at least 2 and at most 256 segments.</summary>
            private static int SegmentCount(double length)
            {
                return Math.Max(2, Math.Min(256, (int)Math.Ceiling(length / 4.0)));
            }
        }
    }
}
