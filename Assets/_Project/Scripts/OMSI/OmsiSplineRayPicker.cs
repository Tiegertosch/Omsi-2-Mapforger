using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A spline as the 3D view's pointer sees it: its centre line from above, how far its cross-section reaches to either side,
    /// and its height along it (ADR 098, M5). Coordinates are map metres (x east, y north) and heights are absolute.
    /// </summary>
    public sealed class OmsiSplineRibbon
    {
        /// <param name="owner">Whatever the caller wants back for a hit.</param>
        /// <param name="x">The centre line, evenly spaced along the spline, so that the height grows evenly from point to point.</param>
        /// <param name="y">As <paramref name="x"/>.</param>
        /// <param name="startHeight">Height at the first point.</param>
        /// <param name="endHeight">Height at the last point.</param>
        /// <param name="left">The left edge of the cross-section, metres right of the centre line, so negative (or 0).</param>
        /// <param name="right">The right edge of the cross-section.</param>
        /// <param name="lift">How far the surface of the cross-section lies above the spline's height.</param>
        public OmsiSplineRibbon(object owner, double[] x, double[] y, double startHeight, double endHeight, double left, double right, double lift)
        {
            if (x == null || y == null || x.Length != y.Length || x.Length < 2)
                throw new ArgumentException("A ribbon needs at least two points with x and y each.");
            Owner = owner;
            X = x;
            Y = y;
            StartHeight = startHeight;
            EndHeight = endHeight;
            Left = Math.Min(0.0, left);
            Right = Math.Max(0.0, right);
            Lift = lift;
            MinX = MaxX = x[0];
            MinY = MaxY = y[0];
            for (var index = 1; index < x.Length; index++)
            {
                MinX = Math.Min(MinX, x[index]);
                MaxX = Math.Max(MaxX, x[index]);
                MinY = Math.Min(MinY, y[index]);
                MaxY = Math.Max(MaxY, y[index]);
            }
            MinHeight = Math.Min(startHeight, endHeight) + lift;
            MaxHeight = Math.Max(startHeight, endHeight) + lift;
        }

        public object Owner { get; }
        public double[] X { get; }
        public double[] Y { get; }
        public double StartHeight { get; }
        public double EndHeight { get; }
        public double Left { get; }
        public double Right { get; }
        public double Lift { get; }
        public double MinX { get; }
        public double MaxX { get; }
        public double MinY { get; }
        public double MaxY { get; }
        public double MinHeight { get; }
        public double MaxHeight { get; }

        /// <summary>The wider side of the cross-section.</summary>
        public double Reach => Math.Max(-Left, Right);
    }

    /// <summary>
    /// Which spline a ray from the 3D camera meets: the ray against the surface of every spline, found by plane steps, nearest first.
    /// Kept apart from Unity so that it can be tested; the map view turns its pointer into the ray (ADR 098, M5).
    /// </summary>
    public static class OmsiSplineRayPicker
    {
        /// <summary>A ray this flat or flatter (per unit length) meets no surface in a useful place.</summary>
        private const double MinimumDescent = 1e-6;

        /// <summary>
        /// Whether the line from (x0, y0) to (x1, y1) touches the rectangle, an end inside it counting (Liang-Barsky). Used by the
        /// selection box, in map metres from above or in screen pixels in 3D.
        /// </summary>
        public static bool SegmentTouchesRectangle(double minX, double minY, double maxX, double maxY, double x0, double y0, double x1, double y1)
        {
            var enter = 0.0;
            var leave = 1.0;
            var dx = x1 - x0;
            var dy = y1 - y0;
            return Clip(-dx, x0 - minX, ref enter, ref leave) && Clip(dx, maxX - x0, ref enter, ref leave) &&
                   Clip(-dy, y0 - minY, ref enter, ref leave) && Clip(dy, maxY - y0, ref enter, ref leave);
        }

        private static bool Clip(double direction, double distance, ref double enter, ref double leave)
        {
            if (Math.Abs(direction) < 1e-12)
                return distance >= 0.0;
            var ratio = distance / direction;
            if (direction < 0.0)
            {
                if (ratio > leave)
                    return false;
                if (ratio > enter)
                    enter = ratio;
            }
            else
            {
                if (ratio < enter)
                    return false;
                if (ratio < leave)
                    leave = ratio;
            }
            return true;
        }

        /// <summary>
        /// The nearest ribbon the ray meets within <paramref name="maxDistance"/>, or null. The surface counts as hit up to
        /// <paramref name="marginMeters"/> beyond its edges, plus <paramref name="marginPerMeter"/> for every metre of ray, which is
        /// the pick radius in pixels at that distance.
        /// </summary>
        /// <param name="originX">The camera, map x.</param>
        /// <param name="originHeight">The camera's height.</param>
        /// <param name="originY">The camera, map y.</param>
        /// <param name="directionX">The ray's direction (of length 1), x.</param>
        /// <param name="directionHeight">The ray's direction, up.</param>
        /// <param name="directionY">The ray's direction, y.</param>
        /// <param name="distance">The ray length at the hit.</param>
        public static OmsiSplineRibbon Pick(IReadOnlyList<OmsiSplineRibbon> ribbons, double originX, double originHeight, double originY,
            double directionX, double directionHeight, double directionY, double maxDistance, double marginMeters, double marginPerMeter,
            out double distance)
        {
            if (ribbons == null)
                throw new ArgumentNullException(nameof(ribbons));
            distance = 0.0;
            // The surfaces lie below the camera and the ray runs down; one that rises would meet them only above the camera.
            if (directionHeight > -MinimumDescent || maxDistance <= 0.0)
                return null;

            OmsiSplineRibbon best = null;
            var bestDistance = maxDistance;
            var slack = marginMeters + marginPerMeter * maxDistance;
            for (var index = 0; index < ribbons.Count; index++)
            {
                var ribbon = ribbons[index];
                // The part of the ray between the ribbon's highest and lowest surface is all that can meet it; the ray is a
                // short stretch on the ground there, so most ribbons are passed over by their bounds.
                var enter = Math.Max(0.0, (ribbon.MaxHeight + slack - originHeight) / directionHeight);
                var leave = Math.Min(bestDistance, (ribbon.MinHeight - slack - originHeight) / directionHeight);
                if (enter > leave)
                    continue;
                var reach = ribbon.Reach + slack;
                if (!SegmentMeetsBox(originX + directionX * enter, originY + directionY * enter, originX + directionX * leave,
                        originY + directionY * leave, ribbon.MinX - reach, ribbon.MinY - reach, ribbon.MaxX + reach, ribbon.MaxY + reach))
                    continue;

                var hit = MeetRibbon(ribbon, originX, originHeight, originY, directionX, directionHeight, directionY, bestDistance,
                    marginMeters, marginPerMeter, out var at);
                if (hit && at < bestDistance)
                {
                    bestDistance = at;
                    best = ribbon;
                }
            }
            distance = best == null ? 0.0 : bestDistance;
            return best;
        }

        private static bool MeetRibbon(OmsiSplineRibbon ribbon, double originX, double originHeight, double originY, double directionX,
            double directionHeight, double directionY, double maxDistance, double marginMeters, double marginPerMeter, out double distance)
        {
            distance = double.MaxValue;
            var last = ribbon.X.Length - 1;
            var found = false;
            for (var index = 0; index < last; index++)
            {
                var x0 = ribbon.X[index];
                var y0 = ribbon.Y[index];
                var ax = ribbon.X[index + 1] - x0;
                var ay = ribbon.Y[index + 1] - y0;
                var lengthSquared = ax * ax + ay * ay;
                if (lengthSquared < 1e-12)
                    continue;
                var length = Math.Sqrt(lengthSquared);
                var low = ribbon.StartHeight + (ribbon.EndHeight - ribbon.StartHeight) * index / last;
                var high = ribbon.StartHeight + (ribbon.EndHeight - ribbon.StartHeight) * (index + 1) / last;

                // The surface of the piece is taken level at its middle height first, then at the height under the point found.
                var at = (low + high) * 0.5 + ribbon.Lift;
                var near = false;
                for (var pass = 0; pass < 3; pass++)
                {
                    var t = (at - originHeight) / directionHeight;
                    if (t < 0.0 || t > maxDistance)
                    {
                        near = false;
                        break;
                    }
                    var px = originX + directionX * t - x0;
                    var py = originY + directionY * t - y0;
                    var share = Math.Max(0.0, Math.Min(1.0, (px * ax + py * ay) / lengthSquared));
                    var offsetX = px - ax * share;
                    var offsetY = py - ay * share;
                    // Right of the direction of travel: heading 0 runs north and right is east.
                    var across = (offsetX * ay - offsetY * ax) / length;
                    var margin = marginMeters + marginPerMeter * t;
                    var edge = across >= 0.0 ? ribbon.Right : -ribbon.Left;
                    near = Math.Sqrt(offsetX * offsetX + offsetY * offsetY) <= edge + margin;
                    if (!near)
                        break;
                    var follow = low + (high - low) * share + ribbon.Lift;
                    if (Math.Abs(follow - at) < 0.01)
                    {
                        at = follow;
                        break;
                    }
                    at = follow;
                }
                if (!near)
                    continue;
                var hitDistance = (at - originHeight) / directionHeight;
                if (hitDistance >= 0.0 && hitDistance < distance)
                {
                    distance = hitDistance;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Whether the line piece reaches into the box (slab test).</summary>
        private static bool SegmentMeetsBox(double x0, double y0, double x1, double y1, double minX, double minY, double maxX, double maxY)
        {
            var low = 0.0;
            var high = 1.0;
            return Clip(x1 - x0, minX - x0, maxX - x0, ref low, ref high) && Clip(y1 - y0, minY - y0, maxY - y0, ref low, ref high);
        }

        private static bool Clip(double delta, double from, double to, ref double low, ref double high)
        {
            if (Math.Abs(delta) < 1e-12)
                return from <= 0.0 && to >= 0.0;
            var a = from / delta;
            var b = to / delta;
            if (a > b)
            {
                var swap = a;
                a = b;
                b = swap;
            }
            low = Math.Max(low, a);
            high = Math.Min(high, b);
            return low <= high;
        }
    }
}
