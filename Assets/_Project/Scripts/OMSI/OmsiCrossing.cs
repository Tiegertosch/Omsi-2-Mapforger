using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A pedestrian crossing over an arm of a junction made here: the arm reaches out far enough for it, the pavement
    /// paths of the arm are cut at its centre and a footpath leads across the carriageway between them, so people can
    /// change the side of the street (and AI crossing it finds a path to follow).
    /// </summary>
    public sealed class OmsiCrossing
    {
        internal const string Prefix = "MapWorks crossings:";

        /// <summary>The distance a new crossing gets between the corner of the junction and its centre.</summary>
        public const double DefaultDistanceMeters = 3.0;

        /// <summary>The width a new crossing gets.</summary>
        public const double DefaultWidthMeters = 3.0;

        public const double ClosestMeters = 2.0;

        public const double FarthestMeters = 15.0;

        public const double NarrowestMeters = 2.0;

        public const double WidestMeters = 6.0;

        /// <summary>Metres out along the arm from where its corner starts to the centre of the crossing.</summary>
        public double DistanceMeters { get; set; } = DefaultDistanceMeters;

        /// <summary>The width of the crossing, across the direction the people walk.</summary>
        public double WidthMeters { get; set; } = DefaultWidthMeters;

        /// <summary>The line of a .sco that keeps the crossings by arm, such as <c>MapWorks crossings: 0 3 3; 2 4 3</c>.</summary>
        public static string Write(IList<OmsiJunctionArm> arms)
        {
            var text = new StringBuilder(Prefix);
            var first = true;
            for (var index = 0; index < arms.Count; index++)
            {
                var crossing = arms[index].Crossing;
                if (crossing == null)
                    continue;
                text.Append(first ? " " : "; ").Append(index.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(OmsiXWriter.Number(crossing.DistanceMeters)).Append(' ').Append(OmsiXWriter.Number(crossing.WidthMeters));
                first = false;
            }
            return first ? null : text.ToString();
        }

        /// <summary>The crossings in the text of a .sco, by arm; empty without the line.</summary>
        public static Dictionary<int, OmsiCrossing> Read(string scoText)
        {
            var crossings = new Dictionary<int, OmsiCrossing>();
            foreach (var raw in (scoText ?? string.Empty).Split('\n'))
            {
                var text = raw.Trim();
                if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;
                foreach (var part in text.Substring(Prefix.Length).Split(';'))
                {
                    var words = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 3 && int.TryParse(words[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var arm) &&
                        double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var distance) &&
                        double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
                        crossings[arm] = new OmsiCrossing { DistanceMeters = ClampDistance(distance), WidthMeters = ClampWidth(width) };
                }
            }
            return crossings;
        }

        /// <summary>
        /// The key a crossing's footpath is bound to a light by, such as <c>x2>x2</c> for the crossing over arm 3. It has the
        /// look of a way through (the line of bindings needs the <c>></c>) but is no lane connection: it never counts as one.
        /// </summary>
        public static string WayKey(int arm)
        {
            var text = "x" + arm.ToString(CultureInfo.InvariantCulture);
            return text + ">" + text;
        }

        public static double ClampDistance(double meters)
        {
            return Math.Max(ClosestMeters, Math.Min(FarthestMeters, meters));
        }

        public static double ClampWidth(double meters)
        {
            return Math.Max(NarrowestMeters, Math.Min(WidestMeters, meters));
        }

        public OmsiCrossing Copy()
        {
            return new OmsiCrossing { DistanceMeters = DistanceMeters, WidthMeters = WidthMeters };
        }
    }
}
