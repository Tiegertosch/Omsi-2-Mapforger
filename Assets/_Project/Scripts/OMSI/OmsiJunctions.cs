using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A connection point of a placed object, from a [splinehelper] of its .sco, in world metres. OMSI maps join roads to
    /// such points without an ID: the spline end lies exactly on the point, with its height and heading, and its neighbour
    /// ID stays 0 (858 of 1,171 points in Berlin-Spandau, all 30 used ones in Grundorf).
    /// </summary>
    public sealed class OmsiJunctionPort
    {
        public OmsiTileSummary Tile { get; internal set; }
        public OmsiObjectRecord Object { get; internal set; }

        /// <summary>The place of the [splinehelper] among those of the object, from 0.</summary>
        public int Index { get; internal set; }

        /// <summary>The spline profile the object suggests for this point; roads of another profile join as well.</summary>
        public string ProfilePath { get; internal set; }

        public OmsiMapPoint Position { get; internal set; }
        public double Height { get; internal set; }

        /// <summary>The heading a road leaves the object with here, pointing away from it.</summary>
        public double OutwardHeadingDegrees { get; internal set; }
    }

    public enum OmsiJunctionPortState
    {
        /// <summary>
        /// A spline end lies on the point with its height and heading, within the tolerances, or a point of another object
        /// meets it face to face, as junctions placed against each other do.
        /// </summary>
        Connected,

        /// <summary>A spline end lies near the point but off in position, height or heading, or is joined to a spline as well.</summary>
        NeedsReview,

        /// <summary>A spline end lies on the point but runs into the object.</summary>
        Invalid,

        /// <summary>No spline end lies near the point.</summary>
        Unconnected
    }

    /// <summary>
    /// How closely a spline end must meet a connection point. These are checks of this editor, not rules of OMSI: real maps
    /// meet their points exactly, so the defaults are tight.
    /// </summary>
    public sealed class OmsiJunctionTolerances
    {
        public double PositionMeters { get; set; } = 0.05;
        public double HeightMeters { get; set; } = 0.05;
        public double HeadingDegrees { get; set; } = 1.0;

        /// <summary>A spline end this close but outside the tolerances needs review; further away the point is unconnected.</summary>
        public double ReviewDistanceMeters { get; set; } = 2.0;
    }

    /// <summary>What a connection point is joined to.</summary>
    public sealed class OmsiJunctionPortCheck
    {
        public OmsiJunctionPort Port { get; internal set; }
        public OmsiJunctionPortState State { get; internal set; }

        /// <summary>The spline end at or nearest to the point, or null when none is near.</summary>
        public OmsiSplineEnd End { get; internal set; }

        /// <summary>The point of another object this one meets face to face, when no spline end lies on it.</summary>
        public OmsiJunctionPort Partner { get; internal set; }

        public double DistanceMeters { get; internal set; }
        public double HeadingOffsetDegrees { get; internal set; }
        public double HeightOffsetMeters { get; internal set; }

        /// <summary>One sentence for the user, such as which spline joins and what is off.</summary>
        public string Message { get; internal set; }
    }

    /// <summary>
    /// The connection points of scenery objects of one OMSI installation, read from their .sco files once per file. An
    /// object without [splinehelper] has no known points; one whose file is missing stays placed and is reported as
    /// unresolved.
    /// </summary>
    public sealed class OmsiJunctionCatalog
    {
        private readonly string installationRoot;
        private readonly Dictionary<string, List<OmsiSplineHelper>> helpers = new Dictionary<string, List<OmsiSplineHelper>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public OmsiJunctionCatalog(string installationRoot)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("The OMSI installation folder is required.", nameof(installationRoot));
            this.installationRoot = Path.GetFullPath(installationRoot);
        }

        /// <summary>The [splinehelper] points of an object file; empty when it has none or cannot be read.</summary>
        public IReadOnlyList<OmsiSplineHelper> Helpers(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return Array.Empty<OmsiSplineHelper>();
            if (helpers.TryGetValue(assetPath, out var cached))
                return cached;
            var result = new List<OmsiSplineHelper>();
            try
            {
                var scoPath = OmsiRelativePath.Resolve(installationRoot, assetPath);
                if (File.Exists(scoPath))
                    result.AddRange(OmsiSceneryObject.Parse(File.ReadAllBytes(scoPath)).SplineHelpers);
                else
                    problems[assetPath] = "Object file not found: " + assetPath;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                problems[assetPath] = assetPath + ": " + exception.Message;
            }
            helpers[assetPath] = result;
            return result;
        }

        /// <summary>Why the object file could not be read, or null when it was read.</summary>
        public string Problem(string assetPath)
        {
            Helpers(assetPath);
            return problems.TryGetValue(assetPath ?? string.Empty, out var problem) ? problem : null;
        }

        /// <summary>The connection points of one placed object, turned with its heading, pitch and bank.</summary>
        public List<OmsiJunctionPort> Ports(OmsiTileSummary tile, OmsiObjectRecord item, double tileSizeMeters)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            var result = new List<OmsiJunctionPort>();
            var list = Helpers(item.AssetPath);
            if (list.Count == 0)
                return result;
            var origin = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, item.LocalX, item.LocalY, tileSizeMeters);
            var bank = item.BankDegrees * Math.PI / 180.0;
            var pitch = item.PitchDegrees * Math.PI / 180.0;
            for (var index = 0; index < list.Count; index++)
            {
                var helper = list[index];
                OmsiTerrainHoleCatalog.Rotate(helper.X, helper.Height, helper.Y, item.HeadingDegrees, item.PitchDegrees, item.BankDegrees,
                    out var east, out var north);
                // The same turns for the height: bank about the forward axis, then pitch, which lowers the front.
                var raised = helper.X * Math.Sin(bank) + helper.Height * Math.Cos(bank);
                var height = raised * Math.Cos(pitch) - helper.Y * Math.Sin(pitch);
                result.Add(new OmsiJunctionPort
                {
                    Tile = tile, Object = item, Index = index, ProfilePath = helper.SplinePath, Position = new OmsiMapPoint(origin.X + east, origin.Y + north),
                    Height = item.Height + height, OutwardHeadingDegrees = Normalize360(item.HeadingDegrees + helper.HeadingDegrees)
                });
            }
            return result;
        }

        /// <summary>The connection points of every placed object of the map.</summary>
        public List<OmsiJunctionPort> AllPorts(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var result = new List<OmsiJunctionPort>();
            foreach (var tile in summary.Tiles)
                foreach (var item in tile.Objects)
                    result.AddRange(Ports(tile, item, summary.TileSizeMeters));
            return result;
        }

        /// <summary>The connection points of the objects of a tile and the tiles around it, such as to find points meeting face to face.</summary>
        public List<OmsiJunctionPort> PortsAround(OmsiMapSummary summary, OmsiTileSummary tile)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            var result = new List<OmsiJunctionPort>();
            foreach (var other in summary.Tiles)
                if (Math.Abs(other.X - tile.X) <= 1 && Math.Abs(other.Y - tile.Y) <= 1)
                    foreach (var item in other.Objects)
                        result.AddRange(Ports(other, item, summary.TileSizeMeters));
            return result;
        }

        /// <summary>
        /// Checks what each connection point is joined to: the spline end on it that runs most nearly along it, or else the
        /// nearest one; without a spline end, a point of another object among <paramref name="others"/> that meets it face
        /// to face.
        /// </summary>
        public static List<OmsiJunctionPortCheck> Check(OmsiMapSummary summary, IEnumerable<OmsiJunctionPort> ports, OmsiJunctionTolerances tolerances = null,
            IEnumerable<OmsiJunctionPort> others = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (ports == null)
                throw new ArgumentNullException(nameof(ports));
            tolerances = tolerances ?? new OmsiJunctionTolerances();
            var size = summary.TileSizeMeters;
            var ends = new List<KeyValuePair<OmsiSplineEnd, OmsiMapPoint>>();
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                    foreach (var atEnd in new[] { false, true })
                    {
                        var end = new OmsiSplineEnd(tile, spline, atEnd);
                        ends.Add(new KeyValuePair<OmsiSplineEnd, OmsiMapPoint>(end, end.Position(size)));
                    }

            var result = new List<OmsiJunctionPortCheck>();
            foreach (var port in ports)
            {
                OmsiSplineEnd nearest = null, along = null;
                double nearestDistance = double.MaxValue, alongOffset = double.MaxValue, alongDistance = 0.0;
                foreach (var pair in ends)
                {
                    var distance = Distance(pair.Value, port.Position);
                    if (distance > tolerances.ReviewDistanceMeters)
                        continue;
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = pair.Key;
                    }
                    var offset = HeadingOffset(port, pair.Key);
                    if (distance <= tolerances.PositionMeters && offset < alongOffset)
                    {
                        alongOffset = offset;
                        along = pair.Key;
                        alongDistance = distance;
                    }
                }
                var chosen = along ?? nearest;
                var check = new OmsiJunctionPortCheck { Port = port, End = chosen };
                var partner = along == null ? Partner(port, others ?? ports, tolerances) : null;
                if (partner != null)
                {
                    check.End = null;
                    check.Partner = partner;
                    check.State = OmsiJunctionPortState.Connected;
                    check.Message = "Point " + (port.Index + 1) + " meets point " + (partner.Index + 1) + " of #" + partner.Object.Id + " directly.";
                    result.Add(check);
                    continue;
                }
                if (chosen == null)
                {
                    check.State = OmsiJunctionPortState.Unconnected;
                    check.Message = "Point " + (port.Index + 1) + " is not connected.";
                    result.Add(check);
                    continue;
                }
                check.DistanceMeters = along != null ? alongDistance : nearestDistance;
                check.HeadingOffsetDegrees = HeadingOffset(port, chosen);
                check.HeightOffsetMeters = chosen.Height - port.Height;
                var name = "Spline #" + chosen.Spline.Id + " (" + (chosen.AtEnd ? "end" : "start") + ")";
                var profile = string.Equals(chosen.Spline.AssetPath, port.ProfilePath, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : "; profile " + Path.GetFileName(chosen.Spline.AssetPath ?? string.Empty) + " instead of " + Path.GetFileName(port.ProfilePath ?? string.Empty);
                if (check.DistanceMeters > tolerances.PositionMeters)
                {
                    check.State = OmsiJunctionPortState.NeedsReview;
                    check.Message = name + " ends " + check.DistanceMeters.ToString("0.00", CultureInfo.InvariantCulture) + " m from point " + (port.Index + 1) + ".";
                }
                else if (check.HeadingOffsetDegrees > 90.0)
                {
                    check.State = OmsiJunctionPortState.Invalid;
                    check.Message = name + " runs into the object at point " + (port.Index + 1) + ".";
                }
                else if (check.HeadingOffsetDegrees > tolerances.HeadingDegrees || Math.Abs(check.HeightOffsetMeters) > tolerances.HeightMeters)
                {
                    check.State = OmsiJunctionPortState.NeedsReview;
                    check.Message = name + " meets point " + (port.Index + 1) + " " + check.HeadingOffsetDegrees.ToString("0.0", CultureInfo.InvariantCulture) + "° off and " +
                                    check.HeightOffsetMeters.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " m in height" + profile + ".";
                }
                else if (!chosen.IsFree)
                {
                    check.State = OmsiJunctionPortState.NeedsReview;
                    check.Message = name + " meets point " + (port.Index + 1) + " but is joined to another spline there as well" + profile + ".";
                }
                else
                {
                    check.State = OmsiJunctionPortState.Connected;
                    check.Message = name + " is connected at point " + (port.Index + 1) + profile + ".";
                }
                result.Add(check);
            }
            return result;
        }

        /// <summary>A point of another object on this one, facing it, at its height, within the tolerances; or null.</summary>
        private static OmsiJunctionPort Partner(OmsiJunctionPort port, IEnumerable<OmsiJunctionPort> others, OmsiJunctionTolerances tolerances)
        {
            foreach (var other in others)
            {
                if (ReferenceEquals(other.Object, port.Object))
                    continue;
                if (Distance(other.Position, port.Position) <= tolerances.PositionMeters &&
                    Math.Abs(other.Height - port.Height) <= tolerances.HeightMeters &&
                    Math.Abs(OmsiRoadPlanner.Normalize(other.OutwardHeadingDegrees + 180.0 - port.OutwardHeadingDegrees)) <= tolerances.HeadingDegrees)
                    return other;
            }
            return null;
        }

        /// <summary>
        /// The free connection point nearest to a world position within <paramref name="maximumDistance"/>: no spline end
        /// lies on it yet and no point of another object meets it. Null when there is none.
        /// </summary>
        public static OmsiJunctionPort FindFree(OmsiMapSummary summary, IEnumerable<OmsiJunctionPort> ports, double worldX, double worldY, double maximumDistance)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (ports == null)
                return null;
            var point = new OmsiMapPoint(worldX, worldY);
            var near = new List<OmsiJunctionPort>();
            foreach (var port in ports)
                if (Distance(port.Position, point) <= maximumDistance)
                    near.Add(port);
            if (near.Count == 0)
                return null;
            OmsiJunctionPort best = null;
            var bestDistance = double.MaxValue;
            foreach (var check in Check(summary, near, null, ports))
            {
                if (check.State != OmsiJunctionPortState.Unconnected && check.DistanceMeters <= new OmsiJunctionTolerances().PositionMeters)
                    continue;
                var distance = Distance(check.Port.Position, point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = check.Port;
                }
            }
            return best;
        }

        /// <summary>How far a spline leaving the point at this end turns from the point's heading, in degrees from 0 to 180.</summary>
        private static double HeadingOffset(OmsiJunctionPort port, OmsiSplineEnd end)
        {
            // The spline leaves the point into itself: opposite to its outward heading at that end.
            return Math.Abs(OmsiRoadPlanner.Normalize(end.OutwardHeadingDegrees + 180.0 - port.OutwardHeadingDegrees));
        }

        private static double Normalize360(double degrees)
        {
            return (degrees % 360.0 + 360.0) % 360.0;
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
    }
}
