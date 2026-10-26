using System;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Where the pointer snaps while a road is drawn (ADR 043): to directions in right angles to a reference, such as the
    /// road it continues or a road nearby, so city blocks come out square.
    /// </summary>
    public static class OmsiRoadSnapping
    {
        /// <summary>A direction this close to a snapped one, in degrees, snaps to it.</summary>
        public const double AngleToleranceDegrees = 4.0;

        /// <summary>
        /// The point on the ray from the origin in the direction of a multiple of <paramref name="stepDegrees"/> from the
        /// reference heading that is nearest the pointer, when the pointer's direction lies within the tolerance of it;
        /// otherwise false and the pointer. Pointers closer than a metre to the origin do not snap.
        /// </summary>
        public static bool SnapToAngle(double originX, double originY, double referenceHeadingDegrees, double stepDegrees,
            double pointerX, double pointerY, out double snappedX, out double snappedY)
        {
            snappedX = pointerX;
            snappedY = pointerY;
            var distance = Math.Sqrt((pointerX - originX) * (pointerX - originX) + (pointerY - originY) * (pointerY - originY));
            if (distance < 1.0 || stepDegrees <= 0.0)
                return false;
            var heading = OmsiRoadPlanner.ChordHeading(originX, originY, pointerX, pointerY);
            var relative = OmsiRoadPlanner.Normalize(heading - referenceHeadingDegrees);
            var snappedRelative = Math.Round(relative / stepDegrees) * stepDegrees;
            if (Math.Abs(relative - snappedRelative) > AngleToleranceDegrees)
                return false;
            var snapped = (referenceHeadingDegrees + snappedRelative) * Math.PI / 180.0;
            // The pointer is projected onto the ray, so the road keeps the length the pointer gives it.
            var along = distance * Math.Cos((relative - snappedRelative) * Math.PI / 180.0);
            snappedX = originX + Math.Sin(snapped) * along;
            snappedY = originY + Math.Cos(snapped) * along;
            return true;
        }

        /// <summary>
        /// For a road that bends tangentially out of its start (<see cref="OmsiRoadShape.Free"/>): the pointer turned about
        /// the start so the road ends straight on or in a right angle to its start heading, when it nearly does. The arc turns
        /// by twice the angle between its heading and the line to its end, so that line snaps in steps of 45°.
        /// </summary>
        public static bool SnapArcEnd(double startX, double startY, double startHeadingDegrees, double pointerX, double pointerY,
            out double snappedX, out double snappedY)
        {
            snappedX = pointerX;
            snappedY = pointerY;
            var distance = Math.Sqrt((pointerX - startX) * (pointerX - startX) + (pointerY - startY) * (pointerY - startY));
            if (distance < 1.0)
                return false;
            var deflection = OmsiRoadPlanner.Normalize(OmsiRoadPlanner.ChordHeading(startX, startY, pointerX, pointerY) - startHeadingDegrees);
            var snappedDeflection = Math.Round(deflection / 45.0) * 45.0;
            // The arc's end heading changes twice as fast as the line to its end.
            if (Math.Abs(snappedDeflection) > 45.0 || Math.Abs(deflection - snappedDeflection) * 2.0 > AngleToleranceDegrees)
                return false;
            var snapped = (startHeadingDegrees + snappedDeflection) * Math.PI / 180.0;
            snappedX = startX + Math.Sin(snapped) * distance;
            snappedY = startY + Math.Cos(snapped) * distance;
            return true;
        }

        /// <summary>
        /// The direction of the road nearest a point within <paramref name="reach"/> metres, where it passes that point; null
        /// when none is that close. A reference for right angles at a free start.
        /// </summary>
        public static double? NearbyRoadHeading(OmsiMapSummary summary, double x, double y, double reach)
        {
            if (summary == null)
                return null;
            var point = OmsiSplinePoint.Nearest(summary, x, y, reach);
            if (point == null)
                return null;
            var spline = point.Spline;
            return spline.RadiusMeters == 0.0
                ? spline.HeadingDegrees
                : spline.HeadingDegrees + point.Distance / spline.RadiusMeters * 180.0 / Math.PI;
        }
    }
}
