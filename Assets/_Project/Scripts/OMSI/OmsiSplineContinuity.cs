using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A connection between two splines whose shared endpoint lies further apart than the tolerance.</summary>
    public sealed class OmsiSplineGap
    {
        public int SplineId { get; internal set; }
        public int NeighbourId { get; internal set; }

        /// <summary>The tile whose file contains <see cref="SplineId"/>.</summary>
        public OmsiTileSummary Tile { get; internal set; }

        public double DistanceMeters { get; internal set; }

        /// <summary>
        /// False when only one of the two splines names the other. Such stale references usually remain after a
        /// spline between them was deleted in the OMSI editor.
        /// </summary>
        public bool IsMutual { get; internal set; }
    }

    /// <summary>Result of comparing the endpoints of every pair of connected splines in a map.</summary>
    public sealed class OmsiSplineContinuityReport
    {
        public double ToleranceMeters { get; internal set; }

        /// <summary>Distinct spline pairs that reference each other and could both be decoded.</summary>
        public int LinkCount { get; internal set; }

        /// <summary>Links whose endpoints meet within <see cref="ToleranceMeters"/>.</summary>
        public int ConnectedCount { get; internal set; }

        /// <summary>Previous/next references to an ID that no decoded spline in the map uses.</summary>
        public int MissingNeighbourCount { get; internal set; }

        /// <summary>Spline IDs that occur in more than one record; only the first record is checked.</summary>
        public int DuplicateIdCount { get; internal set; }

        public double LargestGapMeters { get; internal set; }

        /// <summary>Links outside the tolerance, largest gap first.</summary>
        public List<OmsiSplineGap> Gaps { get; } = new List<OmsiSplineGap>();
    }

    /// <summary>
    /// Checks that connected splines actually meet. Each spline names the spline at its start (previous) and at its
    /// end (next). A neighbour may run in the opposite direction, so its nearer endpoint is used.
    /// </summary>
    public static class OmsiSplineContinuity
    {
        /// <summary>
        /// Default tolerance. Classic maps meet to the millimetre; world-coordinate maps, measured in their Web Mercator
        /// tiles, within a few centimetres.
        /// </summary>
        public const double DefaultToleranceMeters = 0.5;

        private sealed class PlacedSpline
        {
            public OmsiTileSummary Tile;
            public OmsiSplineRecord Record;
            public OmsiMapPoint Start;
            public OmsiMapPoint End;

            /// <summary>The endpoints in metres from the tile corner, for the exact geometry of world-coordinate maps.</summary>
            public OmsiMapPoint LocalStart;
            public OmsiMapPoint LocalEnd;
        }

        public static OmsiSplineContinuityReport Check(OmsiMapSummary summary, double toleranceMeters = DefaultToleranceMeters)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (toleranceMeters < 0.0 || double.IsNaN(toleranceMeters))
                throw new ArgumentOutOfRangeException(nameof(toleranceMeters), "The tolerance must not be negative.");

            var report = new OmsiSplineContinuityReport { ToleranceMeters = toleranceMeters };
            var splinesById = new Dictionary<int, PlacedSpline>();
            foreach (var tile in summary.Tiles)
            {
                foreach (var spline in tile.Splines)
                {
                    if (splinesById.ContainsKey(spline.Id))
                    {
                        report.DuplicateIdCount++;
                        continue;
                    }

                    var placed = new PlacedSpline
                    {
                        Tile = tile,
                        Record = spline,
                        Start = OmsiMapGeometry.GetSplineEndpoint(tile, spline, summary.TileSizeMeters, false),
                        End = OmsiMapGeometry.GetSplineEndpoint(tile, spline, summary.TileSizeMeters, true)
                    };
                    placed.LocalStart = new OmsiMapPoint(placed.Start.X - tile.X * summary.TileSizeMeters, placed.Start.Y - tile.Y * summary.TileSizeMeters);
                    placed.LocalEnd = new OmsiMapPoint(placed.End.X - tile.X * summary.TileSizeMeters, placed.End.Y - tile.Y * summary.TileSizeMeters);
                    splinesById.Add(spline.Id, placed);
                }
            }

            var checkedPairs = new HashSet<long>();
            foreach (var spline in splinesById.Values)
            {
                CheckLink(spline, spline.Record.PreviousId, false, splinesById, checkedPairs, report, summary.UsesWorldCoordinates);
                CheckLink(spline, spline.Record.NextId, true, splinesById, checkedPairs, report, summary.UsesWorldCoordinates);
            }

            report.Gaps.Sort((left, right) => right.DistanceMeters.CompareTo(left.DistanceMeters));
            return report;
        }

        /// <param name="worldTiles">Measures in the Web Mercator tiles of a world-coordinate map instead of the even grid.</param>
        private static void CheckLink(PlacedSpline spline, int neighbourId, bool atEnd,
            Dictionary<int, PlacedSpline> splinesById, HashSet<long> checkedPairs, OmsiSplineContinuityReport report, bool worldTiles)
        {
            if (neighbourId == 0 || neighbourId == spline.Record.Id)
                return;

            if (!splinesById.TryGetValue(neighbourId, out var neighbour))
            {
                report.MissingNeighbourCount++;
                return;
            }

            if (!checkedPairs.Add(PairKey(spline.Record.Id, neighbourId)))
                return;

            var distance = worldTiles
                ? Math.Min(WorldDistance(spline, atEnd, neighbour, false), WorldDistance(spline, atEnd, neighbour, true))
                : Math.Min(Distance(atEnd ? spline.End : spline.Start, neighbour.Start), Distance(atEnd ? spline.End : spline.Start, neighbour.End));
            report.LinkCount++;
            report.LargestGapMeters = Math.Max(report.LargestGapMeters, distance);
            if (distance <= report.ToleranceMeters)
            {
                report.ConnectedCount++;
                return;
            }

            report.Gaps.Add(new OmsiSplineGap
            {
                SplineId = spline.Record.Id,
                NeighbourId = neighbourId,
                Tile = spline.Tile,
                DistanceMeters = distance,
                IsMutual = neighbour.Record.PreviousId == spline.Record.Id || neighbour.Record.NextId == spline.Record.Id
            });
        }

        private static long PairKey(int first, int second)
        {
            var low = Math.Min(first, second);
            var high = Math.Max(first, second);
            return ((long)low << 32) | (uint)high;
        }

        private static double WorldDistance(PlacedSpline first, bool firstEnd, PlacedSpline second, bool secondEnd)
        {
            var a = firstEnd ? first.LocalEnd : first.LocalStart;
            var b = secondEnd ? second.LocalEnd : second.LocalStart;
            return OmsiWorldTiles.DistanceMeters(first.Tile.X, first.Tile.Y, a.X, a.Y, second.Tile.X, second.Tile.Y, b.X, b.Y);
        }

        private static double Distance(OmsiMapPoint first, OmsiMapPoint second)
        {
            var dx = first.X - second.X;
            var dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
