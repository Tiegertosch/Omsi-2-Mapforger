using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The rules of the topology view (contour lines): the height range of a map, when a map counts as flat, and which
    /// line spacing suits a zoom level. Pure logic, no Unity.
    /// </summary>
    public static class OmsiContourRules
    {
        /// <summary>A map whose heights span less than this many metres is flat: it gets no contour lines.</summary>
        public const double FlatSpanMeters = 0.1;

        public const double MinimumInterval = 0.25;
        public const double MaximumInterval = 10.0;

        /// <summary>The spacing used when a manual value is not a number.</summary>
        public const double DefaultInterval = 1.0;

        /// <summary>Every this-many-th line is a major line, drawn stronger.</summary>
        public const int MajorEvery = 5;

        /// <summary>Screen distance in pixels the lines of a gentle slope should keep at least on average.</summary>
        public const double TargetLineDistancePixels = 16.0;

        private static readonly double[] Steps = { 0.25, 0.5, 1.0, 2.0, 2.5, 5.0, 10.0 };

        /// <summary>The "nice" spacings in metres, smallest first.</summary>
        public static IReadOnlyList<double> NiceIntervals => Steps;

        /// <summary>
        /// The lowest and highest height over some terrains. False when there is none or no height is a finite number.
        /// </summary>
        public static bool TryGetRange(IEnumerable<OmsiTerrain> terrains, out double minimum, out double maximum)
        {
            minimum = double.MaxValue;
            maximum = double.MinValue;
            if (terrains == null)
            {
                minimum = 0;
                maximum = 0;
                return false;
            }
            foreach (var terrain in terrains)
            {
                if (terrain == null)
                    continue;
                if (!terrain.TryGetRange(out var low, out var high))
                    continue;
                if (low < minimum)
                    minimum = low;
                if (high > maximum)
                    maximum = high;
            }
            if (minimum > maximum)
            {
                minimum = 0;
                maximum = 0;
                return false;
            }
            return true;
        }

        /// <summary>True when the heights are too even for contour lines (every height of a flat map is the same).</summary>
        public static bool IsFlat(double minimum, double maximum)
        {
            return double.IsNaN(minimum) || double.IsNaN(maximum) || maximum - minimum < FlatSpanMeters;
        }

        /// <summary>A manual spacing kept between <see cref="MinimumInterval"/> and <see cref="MaximumInterval"/>.</summary>
        public static double ClampInterval(double interval)
        {
            if (double.IsNaN(interval) || double.IsInfinity(interval))
                return DefaultInterval;
            return Math.Max(MinimumInterval, Math.Min(MaximumInterval, interval));
        }

        /// <summary>
        /// The spacing for "automatic": the smallest nice step whose lines lie at least
        /// <see cref="TargetLineDistancePixels"/> apart on the screen on a typical slope, but not so coarse that the
        /// height range shows fewer than about four lines. <paramref name="metersPerPixel"/> is the zoom, the
        /// <paramref name="meanSlope"/> (rise over run, for example 0.02) may be 0 to estimate it from the range.
        /// </summary>
        public static double PickInterval(double metersPerPixel, double spanMeters, double meanSlope = 0.0)
        {
            if (double.IsNaN(metersPerPixel) || metersPerPixel <= 0 || double.IsNaN(spanMeters) || spanMeters < FlatSpanMeters)
                return DefaultInterval;
            if (double.IsNaN(meanSlope) || meanSlope <= 0)
                meanSlope = Math.Max(0.005, Math.Min(0.05, spanMeters / 3000.0));

            var wanted = TargetLineDistancePixels * metersPerPixel * meanSlope;
            var chosen = Steps[Steps.Length - 1];
            foreach (var step in Steps)
            {
                if (step >= wanted)
                {
                    chosen = step;
                    break;
                }
            }

            // A small range keeps a finer spacing, so the map still shows some lines.
            var coarsest = Steps[0];
            foreach (var step in Steps)
                if (step <= spanMeters / 4.0)
                    coarsest = step;
            return Math.Min(chosen, coarsest);
        }

        /// <summary>True when the line at this step number (height divided by the spacing, rounded) is a major line.</summary>
        public static bool IsMajorLine(long stepNumber)
        {
            var remainder = stepNumber % MajorEvery;
            return remainder == 0;
        }
    }
}
