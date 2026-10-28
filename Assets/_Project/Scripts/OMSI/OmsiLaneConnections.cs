using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Where a lane of an arm meets a junction made in the editor, at the arm's end, in the object frame (x right,
    /// y forward): a lane leading in, where the junction's lanes start, or one leading out, where they end.
    /// </summary>
    public sealed class OmsiLaneEnd
    {
        /// <summary>The arm, in the order of the junction's connection points.</summary>
        public int Arm { get; internal set; }

        /// <summary>The lane's place among the [path]s of the arm's profile.</summary>
        public int Lane { get; internal set; }

        public bool Inbound { get; internal set; }
        public double X { get; internal set; }
        public double Y { get; internal set; }
        public double Height { get; internal set; }

        /// <summary>The way traffic goes there: into the junction for a lane leading in, out of it for one leading out.</summary>
        public double HeadingDegrees { get; internal set; }
    }

    /// <summary>
    /// Which lane leads to which through a junction made in the editor (ADR 048), modelled on the lane connector of
    /// Traffic for Cities: Skylines II. A connection is named by the arm and profile lane it comes from and goes to, such
    /// as <c>0.1>2.2</c>; the .sco keeps the chosen ones as a line of text. Without the line every lane leading in goes
    /// to every lane leading out on the other arms, as junctions were made before.
    /// </summary>
    public static class OmsiLaneConnections
    {
        internal const string Prefix = "MapWorks lanes:";

        /// <summary>Points along the curves of connections lie this far apart at most.</summary>
        private const double SampleMeters = 0.5;

        public static string Key(int fromArm, int fromLane, int toArm, int toLane)
        {
            return fromArm.ToString(CultureInfo.InvariantCulture) + "." + fromLane.ToString(CultureInfo.InvariantCulture) + ">" +
                   toArm.ToString(CultureInfo.InvariantCulture) + "." + toLane.ToString(CultureInfo.InvariantCulture);
        }

        public static string Key(OmsiLaneEnd from, OmsiLaneEnd to)
        {
            return Key(from.Arm, from.Lane, to.Arm, to.Lane);
        }

        /// <summary>Every way through: each lane leading in to each lane leading out on another arm.</summary>
        public static HashSet<string> All(IList<OmsiLaneEnd> ends)
        {
            var all = new HashSet<string>(StringComparer.Ordinal);
            foreach (var from in ends)
                if (from.Inbound)
                    foreach (var to in ends)
                        if (!to.Inbound && to.Arm != from.Arm)
                            all.Add(Key(from, to));
            return all;
        }

        /// <summary>The line of a .sco that keeps the chosen connections, such as <c>MapWorks lanes: 0.1>1.2 0.1>2.2</c>.</summary>
        public static string Write(IEnumerable<string> connections)
        {
            var sorted = new List<string>(connections);
            sorted.Sort(string.CompareOrdinal);
            var text = new StringBuilder(Prefix);
            foreach (var connection in sorted)
                text.Append(' ').Append(connection);
            return text.ToString();
        }

        /// <summary>The chosen connections in the text of a .sco, or null without the line: every way through.</summary>
        public static HashSet<string> Read(string scoText)
        {
            foreach (var raw in (scoText ?? string.Empty).Split('\n'))
            {
                var text = raw.Trim();
                if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;
                var connections = new HashSet<string>(StringComparer.Ordinal);
                foreach (var word in text.Substring(Prefix.Length).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (word.IndexOf('>') > 0)
                        connections.Add(word);
                return connections;
            }
            return null;
        }

        /// <summary>
        /// The curve a connection makes, as points (x, y) in the object frame: one or two arcs meeting without a kink,
        /// as the junction's lane is built.
        /// </summary>
        public static List<double[]> Curve(OmsiLaneEnd from, OmsiLaneEnd to)
        {
            var points = new List<double[]>();
            foreach (var arc in OmsiRoadPlanner.Biarc(from.X, from.Y, from.HeadingDegrees, to.X, to.Y, to.HeadingDegrees))
            {
                var lane = new OmsiSplineRecord
                {
                    LocalX = arc.StartX, LocalY = arc.StartY, HeadingDegrees = arc.HeadingDegrees, LengthMeters = arc.LengthMeters, RadiusMeters = arc.RadiusMeters
                };
                var count = Math.Max(2, (int)Math.Ceiling(arc.LengthMeters / SampleMeters));
                for (var step = points.Count == 0 ? 0 : 1; step <= count; step++)
                {
                    OmsiMapGeometry.GetLocalFrameAt(lane, arc.LengthMeters * step / count, out var point, out _);
                    points.Add(new[] { point.X, point.Y });
                }
            }
            return points;
        }
    }
}
