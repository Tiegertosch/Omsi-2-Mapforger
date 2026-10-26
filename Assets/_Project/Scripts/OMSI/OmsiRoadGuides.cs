using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A construction line the road tool snaps to (ADR 043): from a point in a direction, only forward or both ways.
    /// </summary>
    public sealed class OmsiGuideLine
    {
        public OmsiGuideLine(double originX, double originY, double headingDegrees, bool bothWays)
        {
            OriginX = originX;
            OriginY = originY;
            HeadingDegrees = headingDegrees;
            BothWays = bothWays;
        }

        public double OriginX { get; }
        public double OriginY { get; }

        /// <summary>The direction; 0 points north and the angle grows clockwise.</summary>
        public double HeadingDegrees { get; }

        /// <summary>True for a whole line through the origin, false for a ray from it.</summary>
        public bool BothWays { get; }

        private double DirectionX => Math.Sin(HeadingDegrees * Math.PI / 180.0);
        private double DirectionY => Math.Cos(HeadingDegrees * Math.PI / 180.0);

        /// <summary>How far along the line a point's foot lies; negative behind a ray's origin.</summary>
        public double Along(double x, double y)
        {
            return (x - OriginX) * DirectionX + (y - OriginY) * DirectionY;
        }

        /// <summary>The point of the line nearest to a point, or false when it lies behind a ray's origin.</summary>
        public bool TryProject(double x, double y, out double footX, out double footY, out double distance)
        {
            var along = Along(x, y);
            footX = OriginX + DirectionX * along;
            footY = OriginY + DirectionY * along;
            distance = Math.Sqrt((x - footX) * (x - footX) + (y - footY) * (y - footY));
            return BothWays || along >= 0.0;
        }

        /// <summary>Where two lines cross, or false when they run (nearly) parallel or cross behind a ray's origin.</summary>
        public bool TryIntersect(OmsiGuideLine other, out double x, out double y)
        {
            x = y = 0.0;
            var cross = DirectionX * other.DirectionY - DirectionY * other.DirectionX;
            // Lines closer than about 6° to each other cross too far from where they would be useful.
            if (Math.Abs(cross) < 0.1)
                return false;
            var t = ((other.OriginX - OriginX) * other.DirectionY - (other.OriginY - OriginY) * other.DirectionX) / cross;
            x = OriginX + DirectionX * t;
            y = OriginY + DirectionY * t;
            return (BothWays || t >= 0.0) && (other.BothWays || other.Along(x, y) >= 0.0);
        }
    }

    /// <summary>
    /// Guide lines of the road network as Cities: Skylines draws them: from each free spline end and each connection
    /// point of a junction, the road's extension and the line square to it through that point. The pointer snaps onto
    /// one, or onto where two cross.
    /// </summary>
    public static class OmsiRoadGuides
    {
        /// <summary>The guide lines of every free spline end and connection point.</summary>
        public static List<OmsiGuideLine> Of(OmsiMapSummary summary, IEnumerable<OmsiJunctionPort> ports)
        {
            var lines = new List<OmsiGuideLine>();
            if (summary == null)
                return lines;
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                    foreach (var atEnd in new[] { false, true })
                    {
                        var end = new OmsiSplineEnd(tile, spline, atEnd);
                        if (end.IsFree)
                            AddPoint(lines, end.Position(summary.TileSizeMeters), end.OutwardHeadingDegrees);
                    }
            if (ports != null)
                foreach (var port in ports)
                    AddPoint(lines, port.Position, port.OutwardHeadingDegrees);
            return lines;
        }

        private static void AddPoint(List<OmsiGuideLine> lines, OmsiMapPoint point, double outwardHeadingDegrees)
        {
            lines.Add(new OmsiGuideLine(point.X, point.Y, outwardHeadingDegrees, false));
            lines.Add(new OmsiGuideLine(point.X, point.Y, outwardHeadingDegrees + 90.0, true));
        }

        /// <summary>
        /// Snaps a point onto the guide lines within <paramref name="tolerance"/> metres whose origin lies within
        /// <paramref name="reach"/>: onto where two of them cross near the point, else onto the nearest. With
        /// <paramref name="fixedLine"/>, such as a right-angle direction the road already snapped to, only where a guide
        /// crosses it counts, so the road keeps that direction. The lines used are added to <paramref name="used"/>.
        /// </summary>
        public static bool Snap(IReadOnlyList<OmsiGuideLine> lines, double x, double y, double tolerance, double reach, OmsiGuideLine fixedLine,
            out double snappedX, out double snappedY, List<OmsiGuideLine> used)
        {
            snappedX = x;
            snappedY = y;
            var near = new List<KeyValuePair<double, OmsiGuideLine>>();
            foreach (var line in lines)
            {
                if ((line.OriginX - x) * (line.OriginX - x) + (line.OriginY - y) * (line.OriginY - y) > reach * reach)
                    continue;
                if (line.TryProject(x, y, out _, out _, out var distance) && distance <= tolerance)
                    near.Add(new KeyValuePair<double, OmsiGuideLine>(distance, line));
            }
            near.Sort((first, second) => first.Key.CompareTo(second.Key));

            // Crossings count within twice the tolerance, so the pointer need not hit both lines exactly.
            var best = double.MaxValue;
            double bestX = x, bestY = y;
            OmsiGuideLine first = null, second = null;
            if (fixedLine != null)
            {
                foreach (var pair in near)
                    if (fixedLine.TryIntersect(pair.Value, out var crossX, out var crossY) && fixedLine.Along(crossX, crossY) >= 1.0)
                        Consider(pair.Value, fixedLine, crossX, crossY);
            }
            else
            {
                for (var i = 0; i < near.Count && i < 8; i++)
                    for (var j = i + 1; j < near.Count && j < 8; j++)
                        if (near[i].Value.TryIntersect(near[j].Value, out var crossX, out var crossY))
                            Consider(near[i].Value, near[j].Value, crossX, crossY);
            }
            if (first != null)
            {
                snappedX = bestX;
                snappedY = bestY;
                used?.Add(first);
                used?.Add(second);
                return true;
            }
            if (fixedLine != null || near.Count == 0)
                return false;
            near[0].Value.TryProject(x, y, out snappedX, out snappedY, out _);
            used?.Add(near[0].Value);
            return true;

            void Consider(OmsiGuideLine a, OmsiGuideLine b, double crossX, double crossY)
            {
                var distance = Math.Sqrt((crossX - x) * (crossX - x) + (crossY - y) * (crossY - y));
                if (distance > 2.0 * tolerance || distance >= best)
                    return;
                best = distance;
                first = a;
                second = b;
                bestX = crossX;
                bestY = crossY;
            }
        }
    }
}
