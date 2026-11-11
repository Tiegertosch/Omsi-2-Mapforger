using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A daily density curve of a map's global.cfg (<c>[trafficdensity_road]</c> for cars, <c>[trafficdensity_passenger]</c> for
    /// people at the stops): points of hour and factor, joined by straight lines, constant before the first and after the last.
    /// A map with no points has a factor of 1 all day. openOMSI reads them the same way (<c>curve_at</c> in omsi-map/global.rs).
    /// </summary>
    public static class OmsiTrafficCurve
    {
        public const string CarSection = "trafficdensity_road";
        public const string PassengerSection = "trafficdensity_passenger";

        /// <summary>The curves are shown and set at every full hour, 0 to 24.</summary>
        public const int HourCount = 25;

        public const double MaximumFactor = 3.0;

        /// <summary>The points of a curve in the file, sorted by hour; empty when the map has none.</summary>
        public static List<KeyValuePair<double, double>> Read(OmsiRecordFile config, string section)
        {
            var points = new List<KeyValuePair<double, double>>();
            foreach (var record in config.Records)
                if (string.Equals(record.Name, section, StringComparison.OrdinalIgnoreCase) && record.Fields.Count >= 2 &&
                    record.TryGetDouble(0, out var hour) && record.TryGetDouble(1, out var factor))
                    points.Add(new KeyValuePair<double, double>(hour, factor));
            points.Sort((a, b) => a.Key.CompareTo(b.Key));
            return points;
        }

        /// <summary>The factor at an hour, as openOMSI computes it.</summary>
        public static double ValueAt(IList<KeyValuePair<double, double>> points, double hour)
        {
            if (points == null || points.Count == 0)
                return 1.0;
            if (hour <= points[0].Key)
                return points[0].Value;
            for (var index = 0; index + 1 < points.Count; index++)
                if (hour >= points[index].Key && hour <= points[index + 1].Key)
                {
                    var span = points[index + 1].Key - points[index].Key;
                    var share = span > 0.0 ? (hour - points[index].Key) / span : 0.0;
                    return points[index].Value + (points[index + 1].Value - points[index].Value) * share;
                }
            return points[points.Count - 1].Value;
        }

        /// <summary>The factor at each full hour 0 to 24.</summary>
        public static double[] Hourly(IList<KeyValuePair<double, double>> points)
        {
            var values = new double[HourCount];
            for (var hour = 0; hour < HourCount; hour++)
                values[hour] = ValueAt(points, hour);
            return values;
        }

        /// <summary>
        /// The points that give the hourly values with the fewest records: the first and last hour, and every hour where the curve
        /// bends (an hour on the straight line between its neighbours, within the three places the file keeps, is left out). Values are rounded to three places.
        /// </summary>
        public static List<KeyValuePair<double, double>> Simplify(double[] hourly)
        {
            if (hourly == null || hourly.Length != HourCount)
                throw new ArgumentException("Expected a value for each hour from 0 to 24.", nameof(hourly));
            var values = new double[HourCount];
            for (var hour = 0; hour < HourCount; hour++)
                values[hour] = Math.Round(Math.Max(0.0, Math.Min(MaximumFactor, hourly[hour])), 3);
            var points = new List<KeyValuePair<double, double>>();
            for (var hour = 0; hour < HourCount; hour++)
            {
                var straight = hour > 0 && hour < HourCount - 1 && Math.Abs(values[hour] - (values[hour - 1] + values[hour + 1]) / 2.0) < 0.0015;
                if (!straight)
                    points.Add(new KeyValuePair<double, double>(hour, values[hour]));
            }
            return points;
        }

        /// <summary>
        /// Replaces the points of a curve: the new ones stand where the first old one stood, and a map that had none gets them
        /// after its other density curves, its seasons or its ground textures.
        /// </summary>
        public static void Write(OmsiRecordFile config, string section, IList<KeyValuePair<double, double>> points)
        {
            var insertAt = -1;
            for (var index = config.Records.Count - 1; index >= 0; index--)
                if (string.Equals(config.Records[index].Name, section, StringComparison.OrdinalIgnoreCase))
                {
                    config.Records.RemoveAt(index);
                    insertAt = index;
                }
            if (insertAt < 0)
            {
                insertAt = config.Records.FindLastIndex(record => record.Name.StartsWith("trafficdensity", StringComparison.OrdinalIgnoreCase) ||
                                                                  string.Equals(record.Name, "addseason", StringComparison.OrdinalIgnoreCase) ||
                                                                  string.Equals(record.Name, "groundtex", StringComparison.OrdinalIgnoreCase)) + 1;
                if (insertAt == 0)
                    insertAt = config.Records.Count;
            }
            for (var index = 0; index < points.Count; index++)
                config.InsertRecord(insertAt + index, config.CreateRecord(section, new[]
                {
                    OmsiNewMapContent.Number(points[index].Key), OmsiNewMapContent.Number(points[index].Value)
                }));
        }
    }
}
