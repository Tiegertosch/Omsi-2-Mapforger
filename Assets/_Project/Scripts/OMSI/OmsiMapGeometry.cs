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

        public static OmsiMapPoint[] BuildSplinePolyline(OmsiTileSummary sourceTile, OmsiSplineRecord spline,
            double tileSizeMeters)
        {
            if (sourceTile == null || spline == null || tileSizeMeters <= 0.0 ||
                spline.LengthMeters <= 0.0 || double.IsNaN(spline.LengthMeters) || double.IsInfinity(spline.LengthMeters))
                return Array.Empty<OmsiMapPoint>();

            var segmentCount = Math.Max(2,
                Math.Min(256, (int)Math.Ceiling(Math.Min(spline.LengthMeters, 2048.0) / 8.0)));
            var step = spline.LengthMeters / segmentCount;
            var localX = spline.LocalX;
            var localY = spline.LocalY;
            var heading = spline.HeadingDegrees * Math.PI / 180.0;
            var points = new OmsiMapPoint[segmentCount + 1];
            points[0] = TileLocalToWorld(sourceTile.X, sourceTile.Y, localX, localY, tileSizeMeters);

            for (var index = 1; index <= segmentCount; index++)
            {
                var radius = spline.RadiusMeters;
                if (Math.Abs(radius) > 0.001)
                {
                    var headingDelta = step / radius;
                    var middleHeading = heading + headingDelta * 0.5;
                    localX += Math.Sin(middleHeading) * step;
                    localY += Math.Cos(middleHeading) * step;
                    heading += headingDelta;
                }
                else
                {
                    localX += Math.Sin(heading) * step;
                    localY += Math.Cos(heading) * step;
                }

                points[index] = TileLocalToWorld(sourceTile.X, sourceTile.Y, localX, localY, tileSizeMeters);
            }

            return points;
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
