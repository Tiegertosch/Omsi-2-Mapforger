using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A point on an AI path a pointer snapped to, with the direction the path runs there.</summary>
    public sealed class OmsiPathSnap
    {
        public double X { get; internal set; }
        public double Y { get; internal set; }

        /// <summary>The heading of the path at the point, clockwise from north, along its points (not turned for its direction).</summary>
        public double HeadingDegrees { get; internal set; }

        /// <summary>How far the pointer was from the path, in metres.</summary>
        public double DistanceMeters { get; internal set; }

        public OmsiMapPath Path { get; internal set; }
    }

    /// <summary>
    /// Snapping to AI paths (ADR 086, step 4): the path tool draws invisible splines whose single path should start and end
    /// on the paths already there, such as the pavement path of a road, so that traffic and people can walk from one onto the other.
    /// </summary>
    public static class OmsiPathSnapping
    {
        /// <summary>The nearest point of a path of the kind within reach of a point, or null for none.</summary>
        /// <param name="kind">0 for roads, 1 for pedestrians, 2 for rails.</param>
        public static OmsiPathSnap Nearest(IEnumerable<OmsiMapPath> paths, int kind, double x, double y, double reach)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));
            OmsiPathSnap best = null;
            foreach (var path in paths)
            {
                if (path.Kind != kind || path.Points == null || path.Points.Length < 2)
                    continue;
                for (var index = 0; index + 1 < path.Points.Length; index++)
                {
                    var a = path.Points[index];
                    var b = path.Points[index + 1];
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var length = dx * dx + dy * dy;
                    if (length < 1e-12)
                        continue;
                    var along = Math.Max(0.0, Math.Min(1.0, ((x - a.X) * dx + (y - a.Y) * dy) / length));
                    var px = a.X + dx * along;
                    var py = a.Y + dy * along;
                    var distance = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                    if (distance > reach || best != null && distance >= best.DistanceMeters)
                        continue;
                    best = new OmsiPathSnap
                    {
                        X = px, Y = py, DistanceMeters = distance, Path = path,
                        HeadingDegrees = (Math.Atan2(dx, dy) * 180.0 / Math.PI + 360.0) % 360.0
                    };
                }
            }
            return best;
        }

        /// <summary>
        /// The kind of path a spline profile is for the path tool, or null: an editor-only profile with exactly one lane,
        /// for people (1) or for road vehicles (0), as OMSI's invisible streets and sidewalks are. Such a spline draws nothing
        /// in the game and only carries its path.
        /// </summary>
        public static int? PathToolKind(OmsiSplineProfile profile)
        {
            if (profile == null || !profile.EditorOnly)
                return null;
            // The example in a profile's comments reads as a lane that cannot be read; it is no lane.
            var lanes = profile.Lanes.FindAll(lane => lane.Kind != OmsiSplineProfile.UnreadableLane);
            if (lanes.Count != 1)
                return null;
            var kind = lanes[0].Kind;
            return kind == 0 || kind == 1 ? (int?)kind : null;
        }
    }
}
