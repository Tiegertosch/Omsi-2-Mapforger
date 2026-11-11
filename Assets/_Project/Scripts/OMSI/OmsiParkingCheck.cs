using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What a parking space lacks to be used by openOMSI's cars, or <see cref="None"/>.</summary>
    public enum OmsiParkingFault
    {
        None,
        NoLane,
        Side,
        Heading,
        LaneEnd
    }

    /// <summary>The verdict on one parking space: the fault, the figure that shows it and where the lane is.</summary>
    public struct OmsiParkingVerdict
    {
        public OmsiParkingFault Fault;

        /// <summary>The distance to the right of the lane for <see cref="OmsiParkingFault.Side"/>, the turn in degrees for <see cref="OmsiParkingFault.Heading"/>.</summary>
        public double Value;
    }

    /// <summary>
    /// Judges a parking space as openOMSI does before a car parks in it: the nearest road lane must have the space 1.2 to 4.2 m to its
    /// right, in line with it within 12 degrees, and run 8 m before the space and 4 m after it.
    /// </summary>
    public static class OmsiParkingCheck
    {
        /// <summary>Lanes farther than this from a space are not looked at.</summary>
        public const double ReachMeters = 30.0;

        public const double MaximumTurnDegrees = 12.0;

        /// <summary>A space farther than this from a lane's middle is no roadside space but part of a car park of its own.</summary>
        public const double RoadsideMeters = 8.0;

        public static OmsiParkingVerdict Judge(double x, double y, double headingDegrees, IEnumerable<OmsiMapPath> paths)
        {
            // The nearest road path, as a point on it, its distance along it and its heading there.
            OmsiMapPath bestPath = null;
            var bestDistance = ReachMeters;
            var bestAlong = 0.0;
            var bestHeading = 0.0;
            var bestFootX = 0.0;
            var bestFootY = 0.0;
            foreach (var path in paths)
            {
                if (path.Kind != 0 || path.Points == null || path.Points.Length < 2)
                    continue;
                var along = 0.0;
                for (var index = 0; index + 1 < path.Points.Length; index++)
                {
                    var a = path.Points[index];
                    var b = path.Points[index + 1];
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var length = Math.Sqrt(dx * dx + dy * dy);
                    if (length < 1e-9)
                        continue;
                    var share = Math.Max(0.0, Math.Min(1.0, ((x - a.X) * dx + (y - a.Y) * dy) / (length * length)));
                    var footX = a.X + dx * share;
                    var footY = a.Y + dy * share;
                    var distance = Math.Sqrt((x - footX) * (x - footX) + (y - footY) * (y - footY));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestPath = path;
                        bestAlong = along + length * share;
                        bestHeading = Math.Atan2(dx, dy) * 180.0 / Math.PI;
                        bestFootX = footX;
                        bestFootY = footY;
                    }
                    along += length;
                }
            }
            if (bestPath == null)
                return new OmsiParkingVerdict { Fault = OmsiParkingFault.NoLane };

            var total = 0.0;
            for (var index = 0; index + 1 < bestPath.Points.Length; index++)
            {
                var dx = bestPath.Points[index + 1].X - bestPath.Points[index].X;
                var dy = bestPath.Points[index + 1].Y - bestPath.Points[index].Y;
                total += Math.Sqrt(dx * dx + dy * dy);
            }
            // A lane for both ways is tried the way that suits the space; a one-way lane runs the way it says.
            var orientations = bestPath.Direction == 2 ? new[] { false, true } : new[] { bestPath.Direction == 1 };
            var first = true;
            var verdict = new OmsiParkingVerdict();
            foreach (var reversed in orientations)
            {
                var heading = reversed ? bestHeading + 180.0 : bestHeading;
                var radians = heading * Math.PI / 180.0;
                var side = (x - bestFootX) * Math.Cos(radians) - (y - bestFootY) * Math.Sin(radians);
                var turn = Normalize180(headingDegrees - heading);
                var alongLane = reversed ? total - bestAlong : bestAlong;
                var current = new OmsiParkingVerdict();
                if (side < OmsiParkingRow.NearestBesideLane || side >= OmsiParkingRow.FarthestBesideLane)
                    current = new OmsiParkingVerdict { Fault = OmsiParkingFault.Side, Value = side };
                else if (Math.Abs(turn) > MaximumTurnDegrees)
                    current = new OmsiParkingVerdict { Fault = OmsiParkingFault.Heading, Value = turn };
                else if (alongLane < OmsiParkingRow.LaneBeforeMeters || alongLane > total - OmsiParkingRow.LaneAfterMeters)
                    current = new OmsiParkingVerdict { Fault = OmsiParkingFault.LaneEnd, Value = alongLane };
                if (current.Fault == OmsiParkingFault.None)
                    return current;
                if (first)
                    verdict = current;
                first = false;
            }
            return verdict;
        }

        private static double Normalize180(double degrees)
        {
            degrees %= 360.0;
            if (degrees > 180.0)
                degrees -= 360.0;
            else if (degrees <= -180.0)
                degrees += 360.0;
            return degrees;
        }
    }
}
