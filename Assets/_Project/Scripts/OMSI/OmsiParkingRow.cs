using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One parking space of a row: where the car park object stands and which way it faces.</summary>
    public sealed class OmsiParkingSpace
    {
        public OmsiMapPoint Position { get; internal set; }

        /// <summary>The heading of the lane beside the space; parked cars stand in line with it.</summary>
        public double HeadingDegrees { get; internal set; }

        /// <summary>The height of the road under the space, for a check; the object itself stands on the terrain.</summary>
        public double RoadHeight { get; internal set; }

        /// <summary>True when openOMSI can send a car into the space once its parked car has left (the lane is long enough there).</summary>
        public bool ParkInPossible { get; internal set; }
    }

    /// <summary>A row of parking spaces along a road, or the reason there is none.</summary>
    public sealed class OmsiParkingPlan
    {
        public List<OmsiParkingSpace> Spaces { get; } = new List<OmsiParkingSpace>();

        /// <summary>Why no row can be made on that side of the road, or null.</summary>
        public string Problem { get; internal set; }
    }

    /// <summary>
    /// Plans a row of parking spaces along a road: car park objects beside the outermost vehicle lane of one side, in line with
    /// it, as openOMSI wants them for cars to park in and out. A space must lie 1.2 to 4.2 m to the right of a lane (as that
    /// lane's driver sees it) and within 12 degrees of its heading, and a lane must run at least 8 m before it and 4 m after.
    /// </summary>
    public static class OmsiParkingRow
    {
        /// <summary>The object of the base game that stands for one parking space of a passenger car.</summary>
        public const string SpaceObject = "Sceneryobjects\\Generic\\car_park.sco";

        /// <summary>The length openOMSI's car park object has (the width of the object is 2 m).</summary>
        public const double SpaceLengthMeters = 4.8;

        public const double SpaceWidthMeters = 2.0;

        /// <summary>Spaces stand this far apart (centre to centre), so cars have room to pull in and out.</summary>
        public const double PitchMeters = 5.5;

        /// <summary>Gap between the edge of the lane and the nearest edge of a space.</summary>
        public const double ClearanceMeters = 0.1;

        /// <summary>openOMSI sends cars into spaces from 1.2 m to 4.2 m beside a lane's middle.</summary>
        public const double NearestBesideLane = 1.2;

        public const double FarthestBesideLane = 4.2;

        /// <summary>A car parks in a space only when the lane has run this far before it and runs this far after it.</summary>
        public const double LaneBeforeMeters = 8.0;

        public const double LaneAfterMeters = 4.0;

        /// <summary>
        /// The spaces along a chain from one distance to another, on its right or left side. The row begins at the nearer
        /// distance and keeps whole spaces only.
        /// </summary>
        public static OmsiParkingPlan Plan(OmsiSplineChain chain, OmsiSplineProfileCatalog profiles, double from, double to, bool right)
        {
            if (chain == null)
                throw new ArgumentNullException(nameof(chain));
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));
            var plan = new OmsiParkingPlan();
            var start = Math.Max(chain.Start, Math.Min(from, to));
            var end = Math.Min(chain.End, Math.Max(from, to));
            if (end - start < SpaceLengthMeters)
            {
                plan.Problem = "short";
                return plan;
            }
            var count = (int)Math.Floor((end - start - (PitchMeters - SpaceLengthMeters)) / PitchMeters);
            for (var index = 0; index < count; index++)
            {
                var distance = start + (PitchMeters - SpaceLengthMeters) / 2.0 + SpaceLengthMeters / 2.0 + index * PitchMeters;
                var segment = chain.At(distance);
                var profile = profiles.Get(segment.Spline.AssetPath);
                if (profile == null)
                {
                    plan.Problem = "no_lane";
                    return plan;
                }
                if (segment.Spline.IsMirrored)
                    profile = profile.Mirrored();
                var lane = OutermostLane(profile, segment.Reversed, right, out var along);
                if (lane == null)
                {
                    plan.Problem = "no_lane";
                    plan.Spaces.Clear();
                    return plan;
                }
                var beside = lane.Width / 2.0 + ClearanceMeters + SpaceWidthMeters / 2.0;
                if (beside > FarthestBesideLane)
                {
                    plan.Problem = "wide";
                    plan.Spaces.Clear();
                    return plan;
                }
                chain.FrameAt(distance, out var centre, out var heading, out var height);
                var radians = heading * Math.PI / 180.0;
                var rightX = Math.Cos(radians);
                var rightY = -Math.Sin(radians);
                // The lane's own offset from the spline, turned into the chain's right-hand frame; the space lies on the lane's right.
                var laneOffset = segment.Reversed ? -lane.X : lane.X;
                var offset = laneOffset + (right ? beside : -beside);
                var local = OmsiSplineChain.LocalDistance(segment, distance);
                // How far along the lane the space is: lanes run with their own direction, from the spline's start when it runs along.
                var runsWithSpline = along != segment.Reversed;
                var into = runsWithSpline ? local : segment.Spline.LengthMeters - local;
                plan.Spaces.Add(new OmsiParkingSpace
                {
                    Position = new OmsiMapPoint(centre.X + rightX * offset, centre.Y + rightY * offset),
                    HeadingDegrees = Normalize(right ? heading : heading + 180.0),
                    RoadHeight = height,
                    ParkInPossible = into >= LaneBeforeMeters && into <= segment.Spline.LengthMeters - LaneAfterMeters
                });
            }
            if (plan.Spaces.Count == 0)
                plan.Problem = "short";
            return plan;
        }

        /// <summary>
        /// The vehicle lane nearest the edge on one side of the chain whose traffic has that side on its right, so a space there
        /// is one to its right. <paramref name="alongChain"/> is true when that lane runs the way the chain does.
        /// </summary>
        private static OmsiSplineLane OutermostLane(OmsiSplineProfile profile, bool reversed, bool right, out bool alongChain)
        {
            OmsiSplineLane best = null;
            alongChain = right;
            foreach (var lane in profile.Lanes)
            {
                if (lane.Kind != 0 || lane.Width <= 0.0)
                    continue;
                // In the chain's frame: its offset to the right and whether it runs along the chain.
                var offset = reversed ? -lane.X : lane.X;
                var alongOfSpline = lane.Direction != 1;
                var runsAlongChain = lane.Direction == 2 ? right : alongOfSpline != reversed;
                if (runsAlongChain != right)
                    continue;
                var onThatSide = right ? offset : -offset;
                var bestOffset = best == null ? double.MinValue : (right ? (reversed ? -best.X : best.X) : -(reversed ? -best.X : best.X));
                if (best == null || onThatSide > bestOffset)
                    best = lane;
            }
            return best;
        }

        /// <summary>
        /// The parked cars a map without a parking list can use: every <c>parked*.sco</c> in a folder of the installation's
        /// Vehicles whose folder also holds an AI car (<c>ai_*.bus</c>), for cars to park and pull out of the spaces.
        /// </summary>
        public static List<string> DefaultParkList(string installationRoot)
        {
            var result = new List<string>();
            var vehicles = System.IO.Path.Combine(installationRoot ?? string.Empty, "Vehicles");
            if (!System.IO.Directory.Exists(vehicles))
                return result;
            var folders = System.IO.Directory.GetDirectories(vehicles);
            Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                if (System.IO.Directory.GetFiles(folder, "ai_*.bus").Length == 0)
                    continue;
                var parked = System.IO.Directory.GetFiles(folder, "*parked*.sco");
                Array.Sort(parked, StringComparer.OrdinalIgnoreCase);
                foreach (var file in parked)
                    result.Add("Vehicles\\" + System.IO.Path.GetFileName(folder) + "\\" + System.IO.Path.GetFileName(file));
            }
            return result;
        }

        private static double Normalize(double degrees)
        {
            degrees %= 360.0;
            return degrees < 0.0 ? degrees + 360.0 : degrees;
        }
    }
}
