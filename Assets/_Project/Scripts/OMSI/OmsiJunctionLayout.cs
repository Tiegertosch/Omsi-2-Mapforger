using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The parts of a road cross-section a junction needs: the carriageway between its kerbs, the pavements beyond them and
    /// the kerb faces, with heights, widths and the textures and texture coordinates the profile uses for them.
    /// </summary>
    public sealed class OmsiProfileSections
    {
        public double RoadHeight { get; private set; }

        /// <summary>
        /// The carriageway reaches this far left and right of the spline, in metres; positive on its own side, negative when
        /// the carriageway lies wholly on the other side, as on one carriageway of a dual road drawn from its median.
        /// </summary>
        public double RoadLeft { get; private set; }

        public double RoadRight { get; private set; }

        /// <summary>The profile ends this far left and right; equal to the carriageway where there is no pavement.</summary>
        public double OuterLeft { get; private set; }

        public double OuterRight { get; private set; }

        public double PavementHeight { get; private set; }

        public int RoadTexture { get; private set; } = -1;

        /// <summary>How the carriageway texture is laid: metres across per unit of u, and v per metre along.</summary>
        public double RoadMetersPerU { get; private set; } = 8.0;

        public double RoadVPerMeter { get; private set; } = 1.0 / 15.0;

        /// <summary>The u of the carriageway texture at <see cref="RoadUAtX"/> metres right of the spline.</summary>
        public double RoadU { get; private set; }

        public double RoadUAtX { get; private set; }
        public int PavementTexture { get; private set; } = -1;
        public double PavementU0 { get; private set; }
        public double PavementU1 { get; private set; }
        public double PavementVPerMeter { get; private set; }
        public int KerbTexture { get; private set; } = -1;
        public double KerbU0 { get; private set; }
        public double KerbU1 { get; private set; }
        public double KerbVPerMeter { get; private set; }

        /// <summary>A rise of more than this is a kerb; less, such as a gutter or the camber of a road, stays carriageway.</summary>
        private const double KerbRise = 0.03;

        /// <summary>
        /// The median between the carriageways of a dual road, its left and right edge in metres right of the spline, at
        /// the kerbs; NaN without one.
        /// </summary>
        public double MedianLeft { get; private set; } = double.NaN;

        public double MedianRight { get; private set; } = double.NaN;

        public bool HasMedian => !double.IsNaN(MedianLeft);

        /// <summary>
        /// The carriageway on one side of the median only, up to its kerb, with no pavement on that side, for a junction
        /// that leaves the median and the far carriageway running through.
        /// </summary>
        internal OmsiProfileSections NearHalf(bool right)
        {
            var half = (OmsiProfileSections)MemberwiseClone();
            if (right)
            {
                half.RoadLeft = -MedianRight;
                half.OuterLeft = half.RoadLeft;
            }
            else
            {
                half.RoadRight = MedianLeft;
                half.OuterRight = half.RoadRight;
            }
            half.MedianLeft = double.NaN;
            half.MedianRight = double.NaN;
            return half;
        }

        public bool HasPavements => PavementTexture >= 0 && (OuterLeft > RoadLeft + 0.05 || OuterRight > RoadRight + 0.05);

        /// <summary>
        /// Reads the sections of a profile. The carriageway reaches over every lane for vehicles, from the outermost on the
        /// left to the outermost on the right, and on over level strips at its height beside them such as gutters; a median
        /// between lanes becomes carriageway, as it does in a junction. Its surface is the strip under the lane nearest the
        /// spline. A kerb is an upright strip, a pavement the raised level strip next to the carriageway. A profile without
        /// lanes for vehicles takes the level run of strips through the spline as carriageway.
        /// </summary>
        public static OmsiProfileSections Of(OmsiSplineProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            var sections = new OmsiProfileSections();
            var level = new List<KeyValuePair<OmsiProfilePoint, OmsiProfilePoint>>();
            var levelTextures = new List<int>();
            foreach (var strip in profile.Strips)
                for (var index = 0; index + 1 < strip.Points.Count; index++)
                {
                    var a = strip.Points[index];
                    var b = strip.Points[index + 1];
                    if (Math.Abs(b.X - a.X) >= OmsiSplineSurface.MinimumWidth)
                    {
                        level.Add(a.X <= b.X ? new KeyValuePair<OmsiProfilePoint, OmsiProfilePoint>(a, b) : new KeyValuePair<OmsiProfilePoint, OmsiProfilePoint>(b, a));
                        levelTextures.Add(strip.TextureIndex);
                    }
                    else if (Math.Abs(b.Height - a.Height) >= OmsiSplineSurface.MinimumWidth && sections.KerbTexture < 0)
                    {
                        sections.KerbTexture = strip.TextureIndex;
                        sections.KerbU0 = a.U;
                        sections.KerbU1 = b.U;
                        sections.KerbVPerMeter = a.VPerMeter;
                    }
                }

            if (level.Count == 0)
                return sections;
            // The surface under the vehicle lane nearest the spline, or the level strip through the spline without lanes.
            double left, right;
            var middle = 0.0;
            var vehicleLanes = profile.Lanes.FindAll(lane => lane.Kind == 0 && lane.Width > 0.0);
            if (vehicleLanes.Count > 0)
            {
                left = double.MaxValue;
                right = double.MinValue;
                middle = vehicleLanes[0].X;
                foreach (var lane in vehicleLanes)
                {
                    left = Math.Min(left, lane.X - lane.Width / 2.0);
                    right = Math.Max(right, lane.X + lane.Width / 2.0);
                    if (Math.Abs(lane.X) < Math.Abs(middle))
                        middle = lane.X;
                }
            }
            else
            {
                left = 0.0;
                right = 0.0;
            }
            var centre = -1;
            var nearest = double.MaxValue;
            for (var index = 0; index < level.Count; index++)
            {
                var gap = Math.Max(0.0, Math.Max(level[index].Key.X - middle, middle - level[index].Value.X));
                if (gap < nearest)
                {
                    nearest = gap;
                    centre = index;
                }
            }
            var height = (level[centre].Key.Height + level[centre].Value.Height) / 2.0;
            left = Math.Min(left, level[centre].Key.X);
            right = Math.Max(right, level[centre].Value.X);
            // Grown outwards over level strips at its height that touch it, such as gutters, up to a kerb or a rise.
            for (var grown = true; grown;)
            {
                grown = false;
                foreach (var pair in level)
                {
                    if (Math.Abs(pair.Key.Height - height) > KerbRise || Math.Abs(pair.Value.Height - height) > KerbRise)
                        continue;
                    if (pair.Value.X >= left - 0.01 && pair.Key.X < left - 0.01)
                    {
                        left = pair.Key.X;
                        grown = true;
                    }
                    if (pair.Key.X <= right + 0.01 && pair.Value.X > right + 0.01)
                    {
                        right = pair.Value.X;
                        grown = true;
                    }
                }
            }
            sections.RoadHeight = height;
            sections.RoadLeft = -left;
            sections.RoadRight = right;

            // A median: the widest gap of at least 0.5 m between neighbouring vehicle lanes running opposite ways, out to the
            // kerbs over gutters at road height; a gap of road surface only, such as a hatched area, is none.
            var sorted = new List<OmsiSplineLane>(vehicleLanes);
            sorted.Sort((a, b) => a.X.CompareTo(b.X));
            double medianLeft = double.NaN, medianRight = double.NaN, widest = 0.5;
            for (var index = 1; index < sorted.Count; index++)
            {
                var a = sorted[index - 1];
                var b = sorted[index];
                var from = a.X + a.Width / 2.0;
                var to = b.X - b.Width / 2.0;
                if (to - from >= widest && a.Direction != b.Direction && a.Direction != 2 && b.Direction != 2)
                {
                    widest = to - from;
                    medianLeft = from;
                    medianRight = to;
                }
            }
            // Road surface at road height across the middle of the gap, such as a hatched area, makes it no median.
            if (!double.IsNaN(medianLeft))
            {
                var middleOfGap = (medianLeft + medianRight) / 2.0;
                if (level.Exists(pair => pair.Key.X <= middleOfGap && pair.Value.X >= middleOfGap && Math.Abs(pair.Key.Height - height) <= KerbRise &&
                                         Math.Abs(pair.Value.Height - height) <= KerbRise))
                    medianLeft = double.NaN;
            }
            if (!double.IsNaN(medianLeft))
            {
                for (var grown = true; grown;)
                {
                    grown = false;
                    foreach (var pair in level)
                    {
                        if (Math.Abs(pair.Key.Height - height) > KerbRise || Math.Abs(pair.Value.Height - height) > KerbRise)
                            continue;
                        if (pair.Key.X <= medianLeft + 0.01 && pair.Value.X > medianLeft + 0.01 && pair.Value.X <= medianRight)
                        {
                            medianLeft = pair.Value.X;
                            grown = true;
                        }
                        if (pair.Value.X >= medianRight - 0.01 && pair.Key.X < medianRight - 0.01 && pair.Key.X >= medianLeft)
                        {
                            medianRight = pair.Key.X;
                            grown = true;
                        }
                    }
                }
                if (medianRight - medianLeft >= 0.3)
                {
                    sections.MedianLeft = medianLeft;
                    sections.MedianRight = medianRight;
                }
            }
            sections.RoadTexture = levelTextures[centre];
            var span = Math.Abs(level[centre].Value.U - level[centre].Key.U);
            if (span > 1e-6)
                sections.RoadMetersPerU = (level[centre].Value.X - level[centre].Key.X) / (level[centre].Value.U - level[centre].Key.U);
            sections.RoadU = level[centre].Key.U;
            sections.RoadUAtX = level[centre].Key.X;
            if (level[centre].Key.VPerMeter > 1e-6)
                sections.RoadVPerMeter = level[centre].Key.VPerMeter;
            sections.OuterLeft = Math.Max(sections.RoadLeft, -profile.LeftEdge);
            sections.OuterRight = Math.Max(sections.RoadRight, profile.RightEdge);

            // A pavement: the raised level strip next to the carriageway, on either side, the nearest one.
            var pavement = -1;
            var pavementGap = 1.0;
            for (var index = 0; index < level.Count; index++)
            {
                var pair = level[index];
                if (Math.Min(pair.Key.Height, pair.Value.Height) <= height + KerbRise)
                    continue;
                var gap = pair.Key.X >= right - 0.05 ? pair.Key.X - right : pair.Value.X <= left + 0.05 ? left - pair.Value.X : double.MaxValue;
                if (gap < pavementGap)
                {
                    pavementGap = gap;
                    pavement = index;
                }
            }
            if (pavement >= 0)
            {
                var pair = level[pavement];
                sections.PavementTexture = levelTextures[pavement];
                sections.PavementHeight = pair.Key.Height;
                var inner = pair.Key.X >= right - 0.05 ? pair.Key : pair.Value;
                var outer = pair.Key.X >= right - 0.05 ? pair.Value : pair.Key;
                sections.PavementU0 = inner.U;
                sections.PavementU1 = outer.U;
                sections.PavementVPerMeter = inner.VPerMeter;
            }
            if (sections.PavementTexture < 0)
            {
                sections.OuterLeft = sections.RoadLeft;
                sections.OuterRight = sections.RoadRight;
                sections.PavementHeight = height;
            }
            return sections;
        }
    }

    /// <summary>An arm of a junction: the heading it leaves with in the object frame and the profile of its road.</summary>
    public sealed class OmsiJunctionArm
    {
        public double HeadingDegrees { get; set; }
        public string ProfilePath { get; set; }
        public OmsiSplineProfile Profile { get; set; }

        /// <summary>
        /// True for the arms of the road going straight through: its surface runs across the junction, and arms of other
        /// roads keep theirs from its kerb outwards. Without one, an arm with an arm opposite counts.
        /// </summary>
        public bool Main { get; set; }

        /// <summary>
        /// True when the road runs towards the junction on this arm, against the arm's heading: its profile is then turned
        /// over as seen from the arm, which matters for profiles that differ left and right.
        /// </summary>
        public bool Mirrored { get; set; }

        /// <summary>Distance from the centre to the connection point, set by the layout.</summary>
        public double Length { get; internal set; }
    }

    /// <summary>A lane or footpath through a junction, as a [path] of the .sco: one arc or straight, in the object frame.</summary>
    public sealed class OmsiJunctionPath
    {
        public double X;
        public double Y;
        public double Height;
        public double HeadingDegrees;
        public double RadiusMeters;
        public double LengthMeters;
        public int Kind;
        public double Width;
        public int Direction;

        /// <summary>0 none, 2 left, 3 right, as OMSI's own junctions mark turning lanes.</summary>
        public int Blinker;
    }

    /// <summary>
    /// The shape of a junction from its arms: a carriageway joining the arms with rounded kerbs between neighbours,
    /// pavements around each corner, kerb faces, a connection point at the end of each arm and lanes for every way through.
    /// Pure geometry in the object frame: x right, y forward (the model's z), heights up; headings clockwise from forward.
    /// </summary>
    public sealed class OmsiJunctionLayout
    {
        /// <summary>The kerb between two neighbouring arms is rounded with this radius.</summary>
        public const double CornerRadius = 6.0;

        /// <summary>Each arm reaches this far past where its corners end, so its cross-section is the road's at the point.</summary>
        public const double ArmExtension = 2.0;

        private const int ArcSegments = 8;

        private OmsiJunctionLayout()
        {
        }

        public List<OmsiJunctionArm> Arms { get; } = new List<OmsiJunctionArm>();

        /// <summary>Carriageway, pavement and kerb surfaces, with the profile texture each uses (index into the arm's textures).</summary>
        public List<OmsiGeneratedMesh> Meshes { get; } = new List<OmsiGeneratedMesh>();

        public List<OmsiJunctionPath> Paths { get; } = new List<OmsiJunctionPath>();

        /// <summary>Texture files the meshes use, relative to the installation, keyed by the file name the meshes name.</summary>
        public Dictionary<string, string> Textures { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, OmsiGeneratedMesh> parts = new Dictionary<string, OmsiGeneratedMesh>(StringComparer.OrdinalIgnoreCase);

        /// <param name="roadTexture">
        /// A plain asphalt texture relative to the installation, or null. It replaces the carriageway texture of the first
        /// arm only where that is asphalt, whose lane markings would otherwise run across the junction; cobbles and other
        /// surfaces keep their own texture.
        /// </param>
        public static OmsiJunctionLayout Build(IEnumerable<OmsiJunctionArm> arms, string roadTexture)
        {
            if (arms == null)
                throw new ArgumentNullException(nameof(arms));
            var layout = new OmsiJunctionLayout();
            // Arms whose road runs towards the junction see their profile turned over.
            foreach (var arm in arms)
                layout.Arms.Add(new OmsiJunctionArm
                {
                    HeadingDegrees = arm.HeadingDegrees, ProfilePath = arm.ProfilePath, Main = arm.Main, Mirrored = arm.Mirrored,
                    Profile = arm.Mirrored && arm.Profile != null ? arm.Profile.Mirrored() : arm.Profile
                });
            if (layout.Arms.Count < 2)
                throw new ArgumentException("A junction needs at least two arms.", nameof(arms));
            layout.Arms.Sort((a, b) => Normalize360(a.HeadingDegrees).CompareTo(Normalize360(b.HeadingDegrees)));
            var sections = new List<OmsiProfileSections>();
            foreach (var arm in layout.Arms)
            {
                if (arm.Profile == null)
                    throw new ArgumentException("Every arm needs a profile.", nameof(arms));
                var section = OmsiProfileSections.Of(arm.Profile);
                if (section.RoadTexture < 0)
                    throw new ArgumentException("The profile " + arm.ProfilePath + " has no carriageway to build a junction from.", nameof(arms));
                sections.Add(section);
            }

            // Two arms facing away from each other make a transition between their cross-sections.
            if (layout.Arms.Count == 2 && layout.Opposite(0, 1))
            {
                layout.BuildTransition(sections, roadTexture);
                return layout;
            }
            var through = layout.TakeNearHalf(sections);

            var count = layout.Arms.Count;
            var corners = new Corner[count];
            for (var index = 0; index < count; index++)
                corners[index] = Corner.Between(layout.Arms[index], sections[index], layout.Arms[(index + 1) % count], sections[(index + 1) % count]);

            // Each arm ends past the farthest point either of its corners reaches along it.
            for (var index = 0; index < count; index++)
            {
                var before = corners[(index - 1 + count) % count];
                var after = corners[index];
                layout.Arms[index].Length = Math.Max(1.0, Math.Max(before.ReachOnSecond, after.ReachOnFirst)) + ArmExtension;
            }
            foreach (var corner in corners)
                corner.Finish();

            layout.BuildCarriageway(sections, corners, roadTexture);
            layout.BuildPavements(sections, corners);
            layout.BuildLanes(sections);
            layout.BuildFootpaths(sections, corners);
            if (through != null)
                layout.BuildThrough(through);
            return layout;
        }

        /// <summary>The part of the main road a junction leaves running through: its profile from one bound to the other, along an arm.</summary>
        private sealed class ThroughPart
        {
            public int Ahead;
            public int Back;
            public double From;
            public double To;
        }

        /// <summary>
        /// On a main road with a median, a junction whose other arms all lie on one side takes only the carriageway on that
        /// side, up to the median, and leaves the median and the far carriageway running through, as junctions on dual
        /// roads are built (left turns would cross the median). Narrows the main arms' sections to that side and returns
        /// what runs through, or null for a junction across the whole road.
        /// </summary>
        private ThroughPart TakeNearHalf(List<OmsiProfileSections> sections)
        {
            var mains = new List<int>();
            var others = new List<int>();
            for (var index = 0; index < Arms.Count; index++)
                (Arms[index].Main ? mains : others).Add(index);
            if (mains.Count != 2 || others.Count == 0 || !Opposite(mains[0], mains[1]))
                return null;
            // The arm the main road runs out along, whose profile is as drawn.
            var ahead = Arms[mains[0]].Mirrored && !Arms[mains[1]].Mirrored ? mains[1] : mains[0];
            var back = ahead == mains[0] ? mains[1] : mains[0];
            var drawn = sections[ahead];
            if (!drawn.HasMedian)
                return null;
            var right = true;
            for (var index = 0; index < others.Count; index++)
            {
                var side = OmsiRoadPlanner.Normalize(Arms[others[index]].HeadingDegrees - Arms[ahead].HeadingDegrees) > 0.0;
                if (index > 0 && side != right)
                    return null;
                right = side;
            }
            foreach (var main in mains)
            {
                var onRight = OmsiRoadPlanner.Normalize(Arms[others[0]].HeadingDegrees - Arms[main].HeadingDegrees) > 0.0;
                if (!sections[main].HasMedian)
                    return null;
                sections[main] = sections[main].NearHalf(onRight);
            }
            var profile = Arms[ahead].Profile;
            return right
                ? new ThroughPart { Ahead = ahead, Back = back, From = profile.LeftEdge, To = drawn.MedianRight }
                : new ThroughPart { Ahead = ahead, Back = back, From = drawn.MedianLeft, To = profile.RightEdge };
        }

        /// <summary>The median and the far carriageway as the road's profile runs, straight through the junction, with their lanes.</summary>
        private void BuildThrough(ThroughPart part)
        {
            var ahead = Arms[part.Ahead];
            var back = Arms[part.Back];
            var clipped = ahead.Profile.Clipped(part.From, part.To);
            var length = ahead.Length + back.Length;
            var start = Point.Along(ahead, -back.Length, 0.0);
            var spline = new OmsiSplineRecord { LocalX = start.X, LocalY = start.Y, HeadingDegrees = ahead.HeadingDegrees, LengthMeters = length };
            foreach (var surface in OmsiSplineSurface.Build(spline, clipped, true))
            {
                var mesh = MeshFor(clipped.Textures[surface.TextureIndex].Path, "through");
                var offset = mesh.Positions.Count / 3;
                mesh.Positions.AddRange(surface.Positions);
                mesh.Uvs.AddRange(surface.Uvs);
                foreach (var index in surface.Triangles)
                    mesh.Triangles.Add(offset + index);
            }
            foreach (var lane in clipped.Lanes)
            {
                var against = lane.Direction == 1;
                var from = Point.Along(ahead, against ? ahead.Length : -back.Length, lane.X);
                Paths.Add(new OmsiJunctionPath
                {
                    X = from.X, Y = from.Y, Height = lane.Height, HeadingDegrees = Normalize360(ahead.HeadingDegrees + (against ? 180.0 : 0.0)),
                    RadiusMeters = 0.0, LengthMeters = length, Kind = lane.Kind, Width = lane.Width, Direction = lane.Direction == 2 ? 2 : 0
                });
            }
        }

        /// <summary>
        /// The length of a transition between two cross-sections: four times the largest change of an edge, at least 8 m
        /// and at most 40 m.
        /// </summary>
        public static double TransitionLength(OmsiProfileSections first, OmsiProfileSections second)
        {
            return Math.Max(8.0, Math.Min(40.0, 4.0 * EdgeChange(first, second)));
        }

        /// <summary>
        /// How far the carriageway or pavement edges of two arms facing away from each other lie apart, at most: zero when
        /// one road goes on into the other without a step.
        /// </summary>
        public static double EdgeChange(OmsiProfileSections first, OmsiProfileSections second)
        {
            // Seen along the first arm, the second arm's left and right swap.
            return Math.Max(Math.Max(Math.Abs(first.RoadLeft - second.RoadRight), Math.Abs(first.RoadRight - second.RoadLeft)),
                Math.Max(Math.Abs(first.OuterLeft - second.OuterRight), Math.Abs(first.OuterRight - second.OuterLeft)));
        }

        /// <summary>
        /// A straight piece from the second arm's cross-section to the first's: carriageway, kerbs and pavements narrow or
        /// widen in an S without a kink at either end, each half of the carriageway in its road's surface; lanes and
        /// footpaths lead to the nearest ones on the other side, so a lane that ends merges into its neighbour.
        /// </summary>
        private void BuildTransition(List<OmsiProfileSections> sections, string roadTexture)
        {
            var first = sections[0];
            var second = sections[1];
            var length = TransitionLength(first, second);
            Arms[0].Length = length / 2.0;
            Arms[1].Length = length / 2.0;
            // Along the first arm, from the second arm's end (share 0) to the first's (share 1); right is the first arm's right.
            const int steps = 16;
            var roadLeft = new List<Point>();
            var roadRight = new List<Point>();
            var outerLeft = new List<Point>();
            var outerRight = new List<Point>();
            var roadHeights = new List<double>();
            var pavementHeights = new List<double>();
            for (var step = 0; step <= steps; step++)
            {
                var share = (double)step / steps;
                var ease = share * share * (3.0 - 2.0 * share);
                var along = -length / 2.0 + length * share;
                roadLeft.Add(Point.Along(Arms[0], along, -Lerp(second.RoadRight, first.RoadLeft, ease)));
                roadRight.Add(Point.Along(Arms[0], along, Lerp(second.RoadLeft, first.RoadRight, ease)));
                outerLeft.Add(Point.Along(Arms[0], along, -Lerp(second.OuterRight, first.OuterLeft, ease)));
                outerRight.Add(Point.Along(Arms[0], along, Lerp(second.OuterLeft, first.OuterRight, ease)));
                roadHeights.Add(Lerp(second.RoadHeight, first.RoadHeight, ease));
                pavementHeights.Add(Lerp(second.PavementHeight, first.PavementHeight, ease));
            }

            // The carriageway: each half in its own road's surface, or one piece when both are alike.
            if (SameSurface(first, Arms[0], second, Arms[1]))
                Surface(Outline(roadLeft, roadRight, 0, steps), 0, first, roadTexture);
            else
            {
                Surface(Outline(roadLeft, roadRight, 0, steps / 2), 1, second, roadTexture);
                Surface(Outline(roadLeft, roadRight, steps / 2, steps), 0, first, roadTexture);
            }

            // Pavements and kerbs on both sides, in the textures of the arm that has pavements.
            var section = first.HasPavements ? first : second;
            var profile = (first.HasPavements ? Arms[0] : Arms[1]).Profile;
            if (section.HasPavements)
            {
                var pavement = MeshFor(profile.Textures[section.PavementTexture].Path, "pavement");
                var kerb = section.KerbTexture >= 0 ? MeshFor(profile.Textures[section.KerbTexture].Path, "kerb") : null;
                Strip(pavement, kerb, section, roadLeft, outerLeft, roadHeights, pavementHeights);
                Strip(pavement, kerb, section, roadRight, outerRight, roadHeights, pavementHeights);
            }

            // Lanes and footpaths both ways, each to the nearest on the other side, and every one on the other side reached.
            Connect(Arms[0], Arms[1], 0);
            Connect(Arms[1], Arms[0], 0);
            Connect(Arms[0], Arms[1], 1);
        }

        private static List<Point> Outline(List<Point> left, List<Point> right, int from, int to)
        {
            var ring = new List<Point>();
            for (var index = from; index <= to; index++)
                ring.Add(left[index]);
            for (var index = to; index >= from; index--)
                ring.Add(right[index]);
            return ring;
        }

        /// <summary>A pavement band between a kerb line and an outer edge, with its kerb face, along a transition.</summary>
        private static void Strip(OmsiGeneratedMesh pavement, OmsiGeneratedMesh kerb, OmsiProfileSections section, List<Point> inner, List<Point> outer,
            List<double> roadHeights, List<double> pavementHeights)
        {
            var along = 0.0;
            for (var point = 0; point < inner.Count; point++)
            {
                if (point > 0)
                    along += inner[point].DistanceTo(inner[point - 1]);
                // Where a side has no pavement the band has no width; such triangles are left out when added.
                var start = pavement.Positions.Count / 3;
                Add(pavement, outer[point], pavementHeights[point], section.PavementU1, along * section.PavementVPerMeter);
                Add(pavement, inner[point], pavementHeights[point], section.PavementU0, along * section.PavementVPerMeter);
                if (point > 0)
                {
                    AddUpward(pavement, start - 2, start - 1, start);
                    AddUpward(pavement, start - 1, start + 1, start);
                }
                if (kerb != null)
                {
                    var kerbStart = kerb.Positions.Count / 3;
                    Add(kerb, inner[point], pavementHeights[point], section.KerbU0, along * section.KerbVPerMeter);
                    Add(kerb, inner[point], roadHeights[point], section.KerbU1, along * section.KerbVPerMeter);
                    if (point > 0)
                    {
                        var towards = inner[point].Minus(outer[point]);
                        AddFacing(kerb, kerbStart - 2, kerbStart - 1, kerbStart, towards);
                        AddFacing(kerb, kerbStart - 1, kerbStart + 1, kerbStart, towards);
                    }
                }
            }
        }

        /// <summary>
        /// Paths of one kind from the arm <paramref name="from"/> through to <paramref name="to"/>: vehicle lanes (kind 0)
        /// one way, from each lane in to the nearest lane out and into every lane out from the nearest lane in; footpaths
        /// (kind 1) once, walked both ways.
        /// </summary>
        private void Connect(OmsiJunctionArm from, OmsiJunctionArm to, int kind)
        {
            // Lanes in on the first arm and out on the second, side by side across the second arm.
            var incoming = from.Profile.Lanes.FindAll(lane => lane.Kind == kind && (kind == 1 || lane.Direction != 0));
            var outgoing = to.Profile.Lanes.FindAll(lane => lane.Kind == kind && (kind == 1 || lane.Direction != 1));
            if (incoming.Count == 0 || outgoing.Count == 0)
                return;
            var pairs = new List<KeyValuePair<OmsiSplineLane, OmsiSplineLane>>();
            foreach (var lane in incoming)
                pairs.Add(new KeyValuePair<OmsiSplineLane, OmsiSplineLane>(lane, Nearest(outgoing, -lane.X)));
            foreach (var lane in outgoing)
                if (!pairs.Exists(pair => pair.Value == lane))
                    pairs.Add(new KeyValuePair<OmsiSplineLane, OmsiSplineLane>(Nearest(incoming, -lane.X), lane));
            foreach (var pair in pairs)
                AddArcs(Point.Along(from, from.Length, pair.Key.X), from.HeadingDegrees + 180.0, Point.Along(to, to.Length, pair.Value.X), to.HeadingDegrees,
                    pair.Key.Height, kind, pair.Key.Width, kind == 1 ? 2 : 0, 0);
        }

        private static OmsiSplineLane Nearest(List<OmsiSplineLane> lanes, double x)
        {
            var best = lanes[0];
            foreach (var lane in lanes)
                if (Math.Abs(lane.X - x) < Math.Abs(best.X - x))
                    best = lane;
            return best;
        }

        private static double Lerp(double from, double to, double share)
        {
            return from + (to - from) * share;
        }

        private void BuildCarriageway(List<OmsiProfileSections> sections, Corner[] corners, string roadTexture)
        {
            var ring = new List<Point>();
            for (var index = 0; index < Arms.Count; index++)
            {
                var arm = Arms[index];
                ring.Add(Point.Along(arm, arm.Length, -sections[index].RoadLeft));
                ring.Add(Point.Along(arm, arm.Length, sections[index].RoadRight));
                foreach (var point in corners[index].Kerb)
                    ring.Add(point);
            }
            // The main road's surface runs across; an arm of another surface keeps its own beyond the main road's kerb on its side.
            var main = MainArm();
            var across = Point.Along(Arms[main], 0.0, 1.0);
            var rest = ring;
            var regions = new List<KeyValuePair<int, List<Point>>>();
            for (var index = 0; index < Arms.Count; index++)
            {
                if (index == main || Opposite(index, main) || SameSurface(sections[index], Arms[index], sections[main], Arms[main]))
                    continue;
                var outward = Point.Along(Arms[index], 1.0, 0.0);
                var side = Math.Sign(outward.X * across.X + outward.Y * across.Y);
                if (side == 0)
                    continue;
                var kerb = side > 0 ? sections[main].RoadRight : sections[main].RoadLeft;
                Func<Point, double> beyond = point => side * (point.X * across.X + point.Y * across.Y) - kerb;
                var own = Clip(rest, beyond);
                rest = Clip(rest, point => -beyond(point));
                if (own.Count >= 3)
                    regions.Add(new KeyValuePair<int, List<Point>>(index, own));
            }
            regions.Insert(0, new KeyValuePair<int, List<Point>>(main, rest));
            foreach (var region in regions)
                Surface(region.Value, region.Key, sections[region.Key], roadTexture);
        }

        /// <summary>The arm whose road goes straight through: one marked main, else one with an arm opposite, else the first.</summary>
        private int MainArm()
        {
            var marked = Arms.FindIndex(arm => arm.Main);
            if (marked >= 0)
                return marked;
            for (var index = 0; index < Arms.Count; index++)
                if (Arms.Exists(other => Opposite(Arms.IndexOf(other), index)))
                    return index;
            return 0;
        }

        private bool Opposite(int first, int second)
        {
            return first != second && Math.Abs(OmsiRoadPlanner.Normalize(Arms[first].HeadingDegrees - Arms[second].HeadingDegrees - 180.0)) < 15.0;
        }

        private static bool SameSurface(OmsiProfileSections first, OmsiJunctionArm firstArm, OmsiProfileSections second, OmsiJunctionArm secondArm)
        {
            return string.Equals(firstArm.Profile.Textures[first.RoadTexture].Path, secondArm.Profile.Textures[second.RoadTexture].Path, StringComparison.OrdinalIgnoreCase) &&
                   Math.Abs(first.RoadMetersPerU - second.RoadMetersPerU) < 1e-6 && Math.Abs(first.RoadHeight - second.RoadHeight) < 1e-6;
        }

        /// <summary>
        /// Lays a part of the carriageway with an arm's surface: its texture, or plain asphalt in place of an asphalt with
        /// markings, at its road height, across and along that arm as its profile lays it, so features such as gutters
        /// meet its kerbs.
        /// </summary>
        private void Surface(List<Point> ring, int arm, OmsiProfileSections section, string roadTexture)
        {
            var own = Arms[arm].Profile.Textures[section.RoadTexture].Path;
            var plain = roadTexture != null && System.IO.Path.GetFileName(own).IndexOf("asph", StringComparison.OrdinalIgnoreCase) >= 0;
            var file = plain ? roadTexture : own;
            // Plain asphalt repeats 8 m across and 15 m along, as road profiles lay it; the profile's own texture as it does.
            var metersPerU = plain ? 8.0 : section.RoadMetersPerU;
            var vPerMeter = plain ? 1.0 / 15.0 : section.RoadVPerMeter;
            var uOffset = plain ? 0.0 : section.RoadU - section.RoadUAtX / metersPerU;
            var across = Point.Along(Arms[arm], 0.0, 1.0);
            var mesh = MeshFor(file, "road");
            var offset = mesh.Positions.Count / 3;
            foreach (var point in ring)
            {
                mesh.Positions.Add(point.X);
                mesh.Positions.Add(section.RoadHeight);
                mesh.Positions.Add(point.Y);
                // Across and along that arm: its right and its heading.
                var right = point.X * across.X + point.Y * across.Y;
                var ahead = point.Y * across.X - point.X * across.Y;
                mesh.Uvs.Add(uOffset + right / metersPerU);
                mesh.Uvs.Add(ahead * vPerMeter);
            }
            foreach (var triangle in Triangulate(ring))
                AddUpward(mesh, offset + triangle[0], offset + triangle[1], offset + triangle[2]);
        }

        /// <summary>The part of a polygon where <paramref name="inside"/> is zero or more (Sutherland-Hodgman against one line).</summary>
        private static List<Point> Clip(List<Point> ring, Func<Point, double> inside)
        {
            var result = new List<Point>();
            for (var index = 0; index < ring.Count; index++)
            {
                var current = ring[index];
                var next = ring[(index + 1) % ring.Count];
                var a = inside(current);
                var b = inside(next);
                if (a >= 0)
                    result.Add(current);
                if (a >= 0 != b >= 0)
                {
                    var t = a / (a - b);
                    result.Add(new Point(current.X + (next.X - current.X) * t, current.Y + (next.Y - current.Y) * t));
                }
            }
            return result;
        }

        /// <summary>
        /// Pavements round each corner, with a kerb face onto the carriageway. Along the straight part of each arm the
        /// pavement is its road's, so whatever its texture shows, such as a cycle path, runs on straight up to the corner;
        /// the rounded part takes the pavement of the side road, a plain one where a main road meets it, else the first
        /// arm's with a pavement. Heights run from one arm's to the other's, so each end meets its road.
        /// </summary>
        private void BuildPavements(List<OmsiProfileSections> sections, Corner[] corners)
        {
            for (var index = 0; index < corners.Length; index++)
            {
                var corner = corners[index];
                var next = (index + 1) % corners.Length;
                var first = sections[index];
                var second = sections[next];
                if (!first.HasPavements && !second.HasPavements || corner.Outer.Count != corner.Kerb.Count)
                    continue;
                // No pavement on either side, as along a median that runs through: no band and no kerb.
                if (first.OuterRight - first.RoadRight < 0.05 && second.OuterLeft - second.RoadLeft < 0.05)
                    continue;
                var round = Arms[index].Main != Arms[next].Main
                    ? (Arms[index].Main ? next : index)
                    : (first.HasPavements ? index : next);
                if (!sections[round].HasPavements)
                    round = round == index ? next : index;

                var full = new List<Point>();
                var outer = new List<Point>();
                full.Add(Point.Along(Arms[index], Arms[index].Length, first.RoadRight));
                full.AddRange(corner.Kerb);
                full.Add(Point.Along(Arms[next], Arms[next].Length, -second.RoadLeft));
                outer.Add(Point.Along(Arms[index], Arms[index].Length, first.OuterRight));
                outer.AddRange(corner.Outer);
                outer.Add(Point.Along(Arms[next], Arms[next].Length, -second.OuterLeft));

                var distances = new double[full.Count];
                for (var point = 1; point < full.Count; point++)
                    distances[point] = distances[point - 1] + full[point].DistanceTo(full[point - 1]);
                var total = distances[full.Count - 1];
                for (var segment = 0; segment + 1 < full.Count; segment++)
                {
                    // The straight part next to each arm is that arm's; the rest is the rounded part's.
                    var owner = segment == 0 ? index : segment == full.Count - 2 ? next : round;
                    if (!sections[owner].HasPavements)
                        owner = round;
                    var section = sections[owner];
                    var profile = Arms[owner].Profile;
                    var pavement = MeshFor(profile.Textures[section.PavementTexture].Path, "pavement");
                    var kerb = section.KerbTexture >= 0 ? MeshFor(profile.Textures[section.KerbTexture].Path, "kerb") : null;
                    var start = pavement.Positions.Count / 3;
                    var kerbStart = kerb != null ? kerb.Positions.Count / 3 : 0;
                    foreach (var point in new[] { segment, segment + 1 })
                    {
                        var share = total > 1e-9 ? distances[point] / total : 0.0;
                        var pavementHeight = first.PavementHeight + (second.PavementHeight - first.PavementHeight) * share;
                        var roadHeight = first.RoadHeight + (second.RoadHeight - first.RoadHeight) * share;
                        Add(pavement, outer[point], pavementHeight, section.PavementU1, distances[point] * section.PavementVPerMeter);
                        Add(pavement, full[point], pavementHeight, section.PavementU0, distances[point] * section.PavementVPerMeter);
                        if (kerb != null)
                        {
                            Add(kerb, full[point], pavementHeight, section.KerbU0, distances[point] * section.KerbVPerMeter);
                            Add(kerb, full[point], roadHeight, section.KerbU1, distances[point] * section.KerbVPerMeter);
                        }
                    }
                    AddUpward(pavement, start, start + 1, start + 2);
                    AddUpward(pavement, start + 1, start + 3, start + 2);
                    if (kerb != null)
                    {
                        // The kerb face looks onto the carriageway, away from the pavement.
                        var towards = full[segment].Minus(outer[segment]);
                        AddFacing(kerb, kerbStart, kerbStart + 1, kerbStart + 2, towards);
                        AddFacing(kerb, kerbStart + 1, kerbStart + 3, kerbStart + 2, towards);
                    }
                }
            }
        }

        /// <summary>
        /// A lane for every way through: from each lane into the junction to each lane out of it on another arm, as one or
        /// two arcs meeting without a kink, with the blinker for the turn.
        /// </summary>
        private void BuildLanes(List<OmsiProfileSections> sections)
        {
            for (var from = 0; from < Arms.Count; from++)
                for (var to = 0; to < Arms.Count; to++)
                {
                    if (from == to)
                        continue;
                    foreach (var inbound in Arms[from].Profile.Lanes)
                    {
                        // Only lanes on the arm's carriageway; on a dual road the far one runs through.
                        if (!Within(inbound, sections[from], false))
                            continue;
                        // Lanes used both ways lead in and out.
                        if (inbound.Kind != 0 || inbound.Direction == 0)
                            continue;
                        foreach (var outbound in Arms[to].Profile.Lanes)
                        {
                            if (!Within(outbound, sections[to], false))
                                continue;
                            if (outbound.Kind != 0 || outbound.Direction == 1)
                                continue;
                            var start = Point.Along(Arms[from], Arms[from].Length, inbound.X);
                            var end = Point.Along(Arms[to], Arms[to].Length, outbound.X);
                            var startHeading = Arms[from].HeadingDegrees + 180.0;
                            var turn = OmsiRoadPlanner.Normalize(Arms[to].HeadingDegrees - startHeading);
                            var blinker = Math.Abs(turn) < 30.0 ? 0 : turn > 0 ? 3 : 2;
                            AddArcs(start, startHeading, end, Arms[to].HeadingDegrees, inbound.Height, inbound.Kind, inbound.Width, 0, blinker);
                        }
                    }
                }
        }

        /// <summary>Footpaths around each corner, from the pavement path of one arm to the one of the next, walked both ways.</summary>
        private void BuildFootpaths(List<OmsiProfileSections> sections, Corner[] corners)
        {
            for (var index = 0; index < Arms.Count; index++)
            {
                var first = Arms[index];
                var second = Arms[(index + 1) % Arms.Count];
                OmsiSplineLane right = null, left = null;
                foreach (var lane in first.Profile.Lanes)
                    if (lane.Kind == 1 && lane.X > 0.0 && Within(lane, sections[index], true) && (right == null || lane.X > right.X))
                        right = lane;
                foreach (var lane in second.Profile.Lanes)
                    if (lane.Kind == 1 && lane.X < 0.0 && Within(lane, sections[(index + 1) % Arms.Count], true) && (left == null || lane.X < left.X))
                        left = lane;
                if (right == null || left == null)
                    continue;
                AddArcs(Point.Along(first, first.Length, right.X), first.HeadingDegrees + 180.0, Point.Along(second, second.Length, left.X), second.HeadingDegrees,
                    right.Height, 1, right.Width, 2, 0);
            }
        }

        /// <summary>True when a lane lies on the arm's carriageway, or for footpaths within its outer edges.</summary>
        private static bool Within(OmsiSplineLane lane, OmsiProfileSections section, bool outer)
        {
            var left = outer ? section.OuterLeft : section.RoadLeft;
            var right = outer ? section.OuterRight : section.RoadRight;
            return lane.X >= -left - 0.01 && lane.X <= right + 0.01;
        }

        private void AddArcs(Point start, double startHeading, Point end, double endHeading, double height, int kind, double width, int direction, int blinker)
        {
            foreach (var arc in OmsiRoadPlanner.Biarc(start.X, start.Y, startHeading, end.X, end.Y, endHeading))
                Paths.Add(new OmsiJunctionPath
                {
                    X = arc.StartX, Y = arc.StartY, Height = height, HeadingDegrees = Normalize360(arc.HeadingDegrees), RadiusMeters = arc.RadiusMeters,
                    LengthMeters = arc.LengthMeters, Kind = kind, Width = width, Direction = direction, Blinker = blinker
                });
        }

        /// <summary>
        /// The mesh for a part of the junction, such as the pavements or the kerbs, with its texture. Parts keep their own
        /// meshes even when they share a texture, since each is built as a strip of its own.
        /// </summary>
        private OmsiGeneratedMesh MeshFor(string texturePath, string part)
        {
            var name = System.IO.Path.GetFileName(texturePath);
            Textures[name] = texturePath;
            var key = part + "|" + name;
            if (parts.TryGetValue(key, out var mesh))
                return mesh;
            var created = new OmsiGeneratedMesh { Texture = name };
            Meshes.Add(created);
            parts.Add(key, created);
            return created;
        }

        private static void Add(OmsiGeneratedMesh mesh, Point point, double height, double u, double v)
        {
            mesh.Positions.Add(point.X);
            mesh.Positions.Add(height);
            mesh.Positions.Add(point.Y);
            mesh.Uvs.Add(u);
            mesh.Uvs.Add(v);
        }

        /// <summary>Adds a triangle facing up, turning it over when its corners run the other way.</summary>
        internal static void AddUpward(OmsiGeneratedMesh mesh, int a, int b, int c)
        {
            double X(int vertex) => mesh.Positions[vertex * 3];
            double Z(int vertex) => mesh.Positions[vertex * 3 + 2];
            var up = (Z(b) - Z(a)) * (X(c) - X(a)) - (X(b) - X(a)) * (Z(c) - Z(a));
            if (Math.Abs(up) < 1e-9)
                return;
            mesh.Triangles.AddRange(up > 0 ? new[] { a, b, c } : new[] { a, c, b });
        }

        /// <summary>Adds an upright triangle facing the given direction in plan.</summary>
        private static void AddFacing(OmsiGeneratedMesh mesh, int a, int b, int c, Point towards)
        {
            double[] P(int vertex) => new[] { mesh.Positions[vertex * 3], mesh.Positions[vertex * 3 + 1], mesh.Positions[vertex * 3 + 2] };
            var pa = P(a);
            var pb = P(b);
            var pc = P(c);
            double ux = pb[0] - pa[0], uy = pb[1] - pa[1], uz = pb[2] - pa[2];
            double vx = pc[0] - pa[0], vy = pc[1] - pa[1], vz = pc[2] - pa[2];
            var nx = uy * vz - uz * vy;
            var nz = ux * vy - uy * vx;
            var facing = nx * towards.X + nz * towards.Y;
            if (Math.Abs(facing) < 1e-9 && Math.Abs(nx) + Math.Abs(nz) < 1e-9)
                return;
            mesh.Triangles.AddRange(facing >= 0 ? new[] { a, b, c } : new[] { a, c, b });
        }

        /// <summary>Ear clipping of a simple polygon; returns index triples into the ring.</summary>
        private static List<int[]> Triangulate(List<Point> ring)
        {
            var result = new List<int[]>();
            var indices = new List<int>();
            for (var index = 0; index < ring.Count; index++)
                if (index == 0 || ring[index].DistanceTo(ring[index - 1]) > 1e-6)
                    indices.Add(index);
            if (indices.Count > 3 && ring[indices[indices.Count - 1]].DistanceTo(ring[indices[0]]) < 1e-6)
                indices.RemoveAt(indices.Count - 1);
            // Counter-clockwise in the x-y plane, so a convex corner turns left.
            var area = 0.0;
            for (var index = 0; index < indices.Count; index++)
            {
                var a = ring[indices[index]];
                var b = ring[indices[(index + 1) % indices.Count]];
                area += a.X * b.Y - b.X * a.Y;
            }
            if (area < 0)
                indices.Reverse();
            var guard = 0;
            while (indices.Count > 3 && guard++ < 10000)
            {
                var clipped = false;
                for (var index = 0; index < indices.Count; index++)
                {
                    var previous = indices[(index - 1 + indices.Count) % indices.Count];
                    var current = indices[index];
                    var next = indices[(index + 1) % indices.Count];
                    var a = ring[previous];
                    var b = ring[current];
                    var c = ring[next];
                    var cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (cross <= 1e-9)
                        continue;
                    var inside = false;
                    foreach (var other in indices)
                    {
                        if (other == previous || other == current || other == next)
                            continue;
                        if (InTriangle(ring[other], a, b, c))
                        {
                            inside = true;
                            break;
                        }
                    }
                    if (inside)
                        continue;
                    result.Add(new[] { previous, current, next });
                    indices.RemoveAt(index);
                    clipped = true;
                    break;
                }
                if (!clipped)
                    break;
            }
            if (indices.Count == 3)
                result.Add(new[] { indices[0], indices[1], indices[2] });
            return result;
        }

        private static bool InTriangle(Point p, Point a, Point b, Point c)
        {
            double Side(Point u, Point v, Point w) => (v.X - u.X) * (w.Y - u.Y) - (v.Y - u.Y) * (w.X - u.X);
            var d1 = Side(a, b, p);
            var d2 = Side(b, c, p);
            var d3 = Side(c, a, p);
            return d1 >= -1e-9 && d2 >= -1e-9 && d3 >= -1e-9;
        }

        private static double Normalize360(double degrees)
        {
            return (degrees % 360.0 + 360.0) % 360.0;
        }

        private struct Point
        {
            public double X;
            public double Y;

            public Point(double x, double y)
            {
                X = x;
                Y = y;
            }

            /// <summary>A point <paramref name="along"/> metres out along an arm and <paramref name="right"/> metres to its right.</summary>
            public static Point Along(OmsiJunctionArm arm, double along, double right)
            {
                var radians = arm.HeadingDegrees * Math.PI / 180.0;
                return new Point(Math.Sin(radians) * along + Math.Cos(radians) * right, Math.Cos(radians) * along - Math.Sin(radians) * right);
            }

            public double DistanceTo(Point other)
            {
                return Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
            }

            public Point Minus(Point other)
            {
                return new Point(X - other.X, Y - other.Y);
            }
        }

        /// <summary>
        /// The corner between an arm and the next one clockwise: the first arm's right kerb and the second's left kerb,
        /// rounded where they meet, and the outer edges of the pavements likewise.
        /// </summary>
        private sealed class Corner
        {
            private OmsiJunctionArm first;
            private Line kerbFirst, kerbSecond, outerFirst, outerSecond;
            private double angle;

            /// <summary>How far along each arm the corner reaches.</summary>
            public double ReachOnFirst;

            public double ReachOnSecond;

            /// <summary>The kerb from the first arm to the second, without the points at the arm ends.</summary>
            public List<Point> Kerb { get; } = new List<Point>();

            public List<Point> Outer { get; } = new List<Point>();

            public static Corner Between(OmsiJunctionArm first, OmsiProfileSections firstSection, OmsiJunctionArm second, OmsiProfileSections secondSection)
            {
                var corner = new Corner { first = first };
                corner.angle = Normalize360(second.HeadingDegrees - first.HeadingDegrees);
                corner.kerbFirst = new Line(first, firstSection.RoadRight);
                corner.kerbSecond = new Line(second, -secondSection.RoadLeft);
                corner.outerFirst = new Line(first, firstSection.OuterRight);
                corner.outerSecond = new Line(second, -secondSection.OuterLeft);
                var widthFirst = firstSection.OuterRight - firstSection.RoadRight;
                var widthSecond = secondSection.OuterLeft - secondSection.RoadLeft;
                corner.Fillet(corner.kerbFirst, corner.kerbSecond, CornerRadius, out var kerbA, out var kerbB);
                corner.Fillet(corner.outerFirst, corner.outerSecond, Math.Max(0.5, CornerRadius - Math.Min(widthFirst, widthSecond)), out var outerA, out var outerB);
                corner.ReachOnFirst = Math.Max(kerbA, outerA);
                corner.ReachOnSecond = Math.Max(kerbB, outerB);
                return corner;
            }

            /// <summary>Fills in the kerb and outer edge once the arms have their lengths.</summary>
            public void Finish()
            {
                Trace(kerbFirst, kerbSecond, CornerRadius, Kerb);
                var widthFirst = outerFirst.Offset - kerbFirst.Offset;
                var widthSecond = -(outerSecond.Offset - kerbSecond.Offset);
                Trace(outerFirst, outerSecond, Math.Max(0.5, CornerRadius - Math.Min(widthFirst, widthSecond)), Outer);
            }

            /// <summary>
            /// Where a fillet of the radius touches each line, as distances along their arms; for arms nearly opposite or
            /// turning away the lines meet at their intersection, or run straight across at the arm base.
            /// </summary>
            private void Fillet(Line a, Line b, double radius, out double alongA, out double alongB)
            {
                if (angle >= 175.0 && angle <= 185.0)
                {
                    alongA = 0.0;
                    alongB = 0.0;
                    return;
                }
                if (!a.Intersect(b, out var tA, out var tB))
                {
                    alongA = 0.0;
                    alongB = 0.0;
                    return;
                }
                if (angle > 185.0)
                {
                    alongA = Math.Max(0.0, tA);
                    alongB = Math.Max(0.0, tB);
                    return;
                }
                var tangent = radius / Math.Tan(angle * Math.PI / 180.0 / 2.0);
                alongA = tA + tangent;
                alongB = tB + tangent;
            }

            private void Trace(Line a, Line b, double radius, List<Point> points)
            {
                if (angle >= 175.0 && angle <= 185.0 || !a.Intersect(b, out var tA, out var tB))
                {
                    points.Add(a.At(0.0));
                    points.Add(b.At(0.0));
                    return;
                }
                if (angle > 185.0)
                {
                    points.Add(a.At(tA));
                    return;
                }
                var tangent = radius / Math.Tan(angle * Math.PI / 180.0 / 2.0);
                var start = a.At(tA + tangent);
                var end = b.At(tB + tangent);
                // The arc's centre lies off the first line by the radius, on the side away from the carriageway.
                var centre = Offset(start, first, radius);
                var from = Math.Atan2(start.X - centre.X, start.Y - centre.Y);
                var to = Math.Atan2(end.X - centre.X, end.Y - centre.Y);
                var sweep = to - from;
                while (sweep > Math.PI)
                    sweep -= 2.0 * Math.PI;
                while (sweep < -Math.PI)
                    sweep += 2.0 * Math.PI;
                for (var step = 0; step <= ArcSegments; step++)
                {
                    var angleNow = from + sweep * step / ArcSegments;
                    points.Add(new Point(centre.X + Math.Sin(angleNow) * radius, centre.Y + Math.Cos(angleNow) * radius));
                }
            }

            /// <summary>A point moved to the right of an arm by a distance.</summary>
            private static Point Offset(Point point, OmsiJunctionArm arm, double right)
            {
                var radians = arm.HeadingDegrees * Math.PI / 180.0;
                return new Point(point.X + Math.Cos(radians) * right, point.Y - Math.Sin(radians) * right);
            }
        }

        /// <summary>A line parallel to an arm at an offset to its right: points are offset × right + t × outward.</summary>
        private struct Line
        {
            private readonly double ox, oy, dx, dy;

            public Line(OmsiJunctionArm arm, double offset)
            {
                var radians = arm.HeadingDegrees * Math.PI / 180.0;
                dx = Math.Sin(radians);
                dy = Math.Cos(radians);
                ox = Math.Cos(radians) * offset;
                oy = -Math.Sin(radians) * offset;
                Offset = offset;
            }

            public double Offset { get; }

            public Point At(double t)
            {
                return new Point(ox + dx * t, oy + dy * t);
            }

            public bool Intersect(Line other, out double t, out double s)
            {
                var denominator = dx * other.dy - dy * other.dx;
                t = 0.0;
                s = 0.0;
                if (Math.Abs(denominator) < 1e-9)
                    return false;
                var ex = other.ox - ox;
                var ey = other.oy - oy;
                t = (ex * other.dy - ey * other.dx) / denominator;
                s = (ex * dy - ey * dx) / denominator;
                return true;
            }
        }
    }
}
