using System;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Shared classic-grid coordinate and spline geometry used by both map overviews.</summary>
    public static class OmsiMapGeometry
    {
        public static OmsiMapPoint TileLocalToWorld(int tileX, int tileY, double localX, double localY,
            double tileSizeMeters)
        {
            return new OmsiMapPoint(tileX * tileSizeMeters + localX, tileY * tileSizeMeters + localY);
        }

        /// <summary>
        /// Samples a spline's plan-view curve in world metres. Points lie exactly on the straight line or
        /// circular arc described by the record; gradient, cant and skew are not part of the plan view.
        /// </summary>
        public static OmsiMapPoint[] BuildSplinePolyline(OmsiTileSummary sourceTile, OmsiSplineRecord spline,
            double tileSizeMeters)
        {
            if (sourceTile == null || spline == null || tileSizeMeters <= 0.0 || !HasValidLength(spline))
                return Array.Empty<OmsiMapPoint>();

            var segmentCount = Math.Max(2,
                Math.Min(256, (int)Math.Ceiling(Math.Min(spline.LengthMeters, 2048.0) / 8.0)));
            var points = new OmsiMapPoint[segmentCount + 1];
            for (var index = 0; index <= segmentCount; index++)
            {
                var local = GetLocalPointAt(spline, spline.LengthMeters * index / segmentCount);
                points[index] = TileLocalToWorld(sourceTile.X, sourceTile.Y, local.X, local.Y, tileSizeMeters);
            }

            return points;
        }

        /// <summary>Returns the world position of a spline's start (distance 0) or end (its full length).</summary>
        public static OmsiMapPoint GetSplineEndpoint(OmsiTileSummary sourceTile, OmsiSplineRecord spline,
            double tileSizeMeters, bool atEnd)
        {
            if (sourceTile == null)
                throw new ArgumentNullException(nameof(sourceTile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));

            var distance = atEnd && HasValidLength(spline) ? spline.LengthMeters : 0.0;
            var local = GetLocalPointAt(spline, distance);
            return TileLocalToWorld(sourceTile.X, sourceTile.Y, local.X, local.Y, tileSizeMeters);
        }

        /// <summary>
        /// Position in tile-local metres after <paramref name="distance"/> metres along the spline. Heading 0 runs
        /// along +Y and 90 along +X; a positive radius turns right, so the heading grows by distance / radius.
        /// </summary>
        private static OmsiMapPoint GetLocalPointAt(OmsiSplineRecord spline, double distance)
        {
            var heading = spline.HeadingDegrees * Math.PI / 180.0;
            var radius = spline.RadiusMeters;
            if (Math.Abs(radius) <= 0.001)
                return new OmsiMapPoint(spline.LocalX + Math.Sin(heading) * distance,
                    spline.LocalY + Math.Cos(heading) * distance);

            var endHeading = heading + distance / radius;
            return new OmsiMapPoint(spline.LocalX + radius * (Math.Cos(heading) - Math.Cos(endHeading)),
                spline.LocalY + radius * (Math.Sin(endHeading) - Math.Sin(heading)));
        }

        private static bool HasValidLength(OmsiSplineRecord spline)
        {
            return spline.LengthMeters > 0.0 && !double.IsNaN(spline.LengthMeters) && !double.IsInfinity(spline.LengthMeters);
        }

        /// <summary>Clips a world-space line to one tile and returns its endpoints in tile-local metres.</summary>
        public static bool TryClipLineToTile(OmsiMapPoint start, OmsiMapPoint end, int tileX, int tileY,
            double tileSizeMeters, out OmsiMapPoint clippedStart, out OmsiMapPoint clippedEnd)
        {
            clippedStart = default(OmsiMapPoint);
            clippedEnd = default(OmsiMapPoint);
            if (tileSizeMeters <= 0.0)
                return false;

            var minX = tileX * tileSizeMeters;
            var minY = tileY * tileSizeMeters;
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var t0 = 0.0;
            var t1 = 1.0;
            if (!Clip(-dx, start.X - minX, ref t0, ref t1) ||
                !Clip(dx, minX + tileSizeMeters - start.X, ref t0, ref t1) ||
                !Clip(-dy, start.Y - minY, ref t0, ref t1) ||
                !Clip(dy, minY + tileSizeMeters - start.Y, ref t0, ref t1))
                return false;

            clippedStart = new OmsiMapPoint(start.X + t0 * dx - minX, start.Y + t0 * dy - minY);
            clippedEnd = new OmsiMapPoint(start.X + t1 * dx - minX, start.Y + t1 * dy - minY);
            return true;
        }

        private static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (Math.Abs(p) < 1e-12)
                return q >= 0.0;

            var ratio = q / p;
            if (p < 0.0)
            {
                if (ratio > t1) return false;
                if (ratio > t0) t0 = ratio;
            }
            else
            {
                if (ratio < t0) return false;
                if (ratio < t1) t1 = ratio;
            }

            return true;
        }
    }

    public struct OmsiMapPoint
    {
        public readonly double X;
        public readonly double Y;

        public OmsiMapPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
