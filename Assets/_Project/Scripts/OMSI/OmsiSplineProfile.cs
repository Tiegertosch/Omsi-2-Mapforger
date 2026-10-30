using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A texture of a spline profile.</summary>
    public sealed class OmsiSplineTexture
    {
        /// <summary>Path relative to the OMSI installation: the <c>texture</c> folder next to the .sli file.</summary>
        public string Path { get; internal set; }

        /// <summary>True when [matl_alpha] follows the texture, so its alpha channel cuts or blends.</summary>
        public bool UsesAlpha { get; internal set; }
    }

    /// <summary>How a spline without a surface seen from above is shown there.</summary>
    public enum OmsiSplineLineKind
    {
        /// <summary>The spline has a surface from above, or is a helper that only the OMSI editor shows.</summary>
        None,

        /// <summary>Wires above the ground, such as overhead lines, catenary and cross spans: narrow and at least 2.5 m up.</summary>
        Overhead,

        /// <summary>Narrow upright profiles from the ground, such as fences and walls.</summary>
        Fence
    }

    /// <summary>One point of a cross-section, left to right; x is positive to the right of the spline.</summary>
    public struct OmsiProfilePoint
    {
        public double X;
        public double Height;
        public double U;

        /// <summary>V grows by this much per metre along the spline; V is 0 at the spline start.</summary>
        public double VPerMeter;
    }

    /// <summary>A strip of the cross-section with one texture; extruded along the spline.</summary>
    public sealed class OmsiProfileStrip
    {
        public int TextureIndex { get; internal set; }
        public List<OmsiProfilePoint> Points { get; } = new List<OmsiProfilePoint>();
    }

    /// <summary>A lane of a spline type: [path] in the .sli, which vehicles and pedestrians follow.</summary>
    public sealed class OmsiSplineLane
    {
        /// <summary>0 for roads, 1 for pedestrians, 2 for rails; <see cref="OmsiSplineProfile.UnreadableLane"/> when unreadable.</summary>
        public int Kind { get; internal set; }

        /// <summary>Distance from the spline, positive to the right.</summary>
        public double X { get; internal set; }

        public double Height { get; internal set; }
        public double Width { get; internal set; }

        /// <summary>0 along the spline, 1 against it, 2 both ways.</summary>
        public int Direction { get; internal set; }
    }

    /// <summary>The graphical cross-section of a spline type (.sli): textures and the strips that use them.</summary>
    public sealed class OmsiSplineProfile
    {
        public List<OmsiSplineTexture> Textures { get; } = new List<OmsiSplineTexture>();
        public List<OmsiProfileStrip> Strips { get; } = new List<OmsiProfileStrip>();

        /// <summary>True for helper splines such as invisible paths, which only the OMSI editor shows.</summary>
        public bool EditorOnly { get; internal set; }

        /// <summary>
        /// For narrow splines that show little or nothing from above, whether they hang overhead like wires or stand on
        /// the ground like fences and walls; <see cref="OmsiSplineLineKind.None"/> for all others.
        /// </summary>
        public OmsiSplineLineKind LineKind { get; private set; }

        /// <summary>Strips at least this high everywhere count as overhead wires.</summary>
        public const double OverheadHeight = 2.5;

        /// <summary>
        /// [terrainholeprofile]: cross-sections that OMSI cuts out of the terrain along the spline, such as rail cuttings;
        /// the lateral positions of their [terrainholeprofilepnt] points, left to right.
        /// </summary>
        public List<List<double>> TerrainHoles { get; } = new List<List<double>>();

        /// <summary>The leftmost and rightmost point of the cross-section, metres from the spline; 0 when it has no strips.</summary>
        public double LeftEdge { get; private set; }

        public double RightEdge { get; private set; }

        /// <summary>The height of the cross-section at its outer edges, such as the top of a kerb, averaged over both sides.</summary>
        public double EdgeHeight { get; private set; }

        /// <summary>The kind of a lane whose [path] could not be read.</summary>
        public const int UnreadableLane = -1;

        /// <summary>The lanes in the order of the file; timetables name them by this index.</summary>
        public List<OmsiSplineLane> Lanes { get; } = new List<OmsiSplineLane>();

        /// <summary>
        /// The cross-section turned over, as a spline with "mirror" or one running the other way shows it: left and right
        /// swap, and lanes along the spline run against it and the other way round.
        /// </summary>
        public OmsiSplineProfile Mirrored()
        {
            var result = new OmsiSplineProfile
            {
                EditorOnly = EditorOnly, LineKind = LineKind, LeftEdge = -RightEdge, RightEdge = -LeftEdge, EdgeHeight = EdgeHeight
            };
            result.Textures.AddRange(Textures);
            foreach (var strip in Strips)
            {
                var turned = new OmsiProfileStrip { TextureIndex = strip.TextureIndex };
                for (var index = strip.Points.Count - 1; index >= 0; index--)
                {
                    var point = strip.Points[index];
                    point.X = -point.X;
                    turned.Points.Add(point);
                }
                result.Strips.Add(turned);
            }
            foreach (var hole in TerrainHoles)
            {
                var turned = new List<double>();
                for (var index = hole.Count - 1; index >= 0; index--)
                    turned.Add(-hole[index]);
                result.TerrainHoles.Add(turned);
            }
            foreach (var lane in Lanes)
                result.Lanes.Add(new OmsiSplineLane
                {
                    Kind = lane.Kind, X = -lane.X, Height = lane.Height, Width = lane.Width,
                    Direction = lane.Direction == 0 ? 1 : lane.Direction == 1 ? 0 : lane.Direction
                });
            return result;
        }

        /// <summary>
        /// The part of the cross-section between two distances right of the spline: strips cut at the bounds, upright
        /// strips within them, and the lanes whose middle lies within them.
        /// </summary>
        public OmsiSplineProfile Clipped(double from, double to)
        {
            var result = new OmsiSplineProfile { EditorOnly = EditorOnly, LineKind = LineKind, EdgeHeight = EdgeHeight };
            result.Textures.AddRange(Textures);
            double left = double.MaxValue, right = double.MinValue;
            foreach (var strip in Strips)
                for (var index = 0; index + 1 < strip.Points.Count; index++)
                {
                    var a = strip.Points[index];
                    var b = strip.Points[index + 1];
                    var low = Math.Min(a.X, b.X);
                    var high = Math.Max(a.X, b.X);
                    if (high < from - 1e-6 || low > to + 1e-6)
                        continue;
                    var piece = new OmsiProfileStrip { TextureIndex = strip.TextureIndex };
                    if (Math.Abs(b.X - a.X) < 1e-9)
                    {
                        // An upright strip, such as a kerb face, lies within the bounds whole.
                        piece.Points.Add(a);
                        piece.Points.Add(b);
                    }
                    else
                    {
                        piece.Points.Add(Cut(a, b, Math.Max(from, Math.Min(to, a.X))));
                        piece.Points.Add(Cut(a, b, Math.Max(from, Math.Min(to, b.X))));
                    }
                    result.Strips.Add(piece);
                    left = Math.Min(left, Math.Max(from, low));
                    right = Math.Max(right, Math.Min(to, high));
                }
            result.LeftEdge = left == double.MaxValue ? 0.0 : left;
            result.RightEdge = right == double.MinValue ? 0.0 : right;
            foreach (var lane in Lanes)
                if (lane.X >= from && lane.X <= to)
                    result.Lanes.Add(lane);
            return result;
        }

        /// <summary>The point of a profile segment at a distance right of the spline, its height and texture coordinates in proportion.</summary>
        private static OmsiProfilePoint Cut(OmsiProfilePoint a, OmsiProfilePoint b, double x)
        {
            if (Math.Abs(b.X - a.X) < 1e-9)
                return new OmsiProfilePoint { X = x, Height = a.Height, U = a.U, VPerMeter = a.VPerMeter };
            var share = (x - a.X) / (b.X - a.X);
            return new OmsiProfilePoint
            {
                X = x, Height = a.Height + (b.Height - a.Height) * share, U = a.U + (b.U - a.U) * share, VPerMeter = a.VPerMeter + (b.VPerMeter - a.VPerMeter) * share
            };
        }

        /// <summary>
        /// Reads a .sli file. Texture names are resolved against the <c>texture</c> folder next to
        /// <paramref name="assetPath"/>, the path the tile files use.
        /// </summary>
        public static OmsiSplineProfile Parse(byte[] bytes, string assetPath)
        {
            var file = OmsiRecordFile.Parse(bytes, null);
            var folder = System.IO.Path.GetDirectoryName(assetPath ?? string.Empty) ?? string.Empty;
            var result = new OmsiSplineProfile();
            OmsiProfileStrip strip = null;
            foreach (var record in file.Records)
            {
                // Keywords are followed by their values; comments come after them and are ignored.
                var values = record.AllValues();
                switch (record.Name.ToLowerInvariant())
                {
                    case "onlyeditor":
                        result.EditorOnly = true;
                        break;
                    case "texture":
                        if (values.Count > 0)
                            result.Textures.Add(new OmsiSplineTexture { Path = System.IO.Path.Combine(folder, "texture", values[0].Trim()) });
                        break;
                    case "matl_alpha":
                        if (result.Textures.Count > 0 && values.Count > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var alpha))
                            result.Textures[result.Textures.Count - 1].UsesAlpha = alpha > 0;
                        break;
                    case "profile":
                        strip = values.Count > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var texture)
                            ? new OmsiProfileStrip { TextureIndex = texture }
                            : null;
                        if (strip != null)
                            result.Strips.Add(strip);
                        break;
                    case "terrainholeprofile":
                        result.TerrainHoles.Add(new List<double>());
                        break;
                    case "terrainholeprofilepnt":
                        // Points are x, z, y; only the lateral x matters for the shape seen from above.
                        if (result.TerrainHoles.Count > 0 && values.Count > 0 &&
                            double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var holeX))
                            result.TerrainHoles[result.TerrainHoles.Count - 1].Add(holeX);
                        break;
                    case "path":
                        // Kind, x, height, width and direction. A lane that cannot be read, such as the [path] that opens a
                        // commented explanation in some rail profiles, keeps the index of the next with a kind of its own.
                        var lane = new OmsiSplineLane { Kind = UnreadableLane };
                        if (values.Count >= 5 && TryNumbers(values, out var laneValues))
                        {
                            lane.Kind = (int)laneValues[0];
                            lane.X = laneValues[1];
                            lane.Height = laneValues[2];
                            lane.Width = laneValues[3];
                        }
                        if (values.Count >= 5 && int.TryParse(values[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var direction))
                            lane.Direction = direction;
                        result.Lanes.Add(lane);
                        break;
                    case "profilepnt":
                        if (strip != null && TryNumbers(values, out var numbers))
                            strip.Points.Add(new OmsiProfilePoint { X = numbers[0], Height = numbers[1], U = numbers[2], VPerMeter = numbers[3] });
                        break;
                }
            }
            result.Strips.RemoveAll(item => item.Points.Count < 2);
            result.TerrainHoles.RemoveAll(item => item.Count < 2);
            result.LineKind = Classify(result);
            MeasureEdges(result);
            return result;
        }

        private static void MeasureEdges(OmsiSplineProfile profile)
        {
            OmsiProfilePoint? left = null, right = null;
            foreach (var strip in profile.Strips)
                foreach (var point in strip.Points)
                {
                    if (!left.HasValue || point.X < left.Value.X)
                        left = point;
                    if (!right.HasValue || point.X > right.Value.X)
                        right = point;
                }
            if (!left.HasValue)
                return;
            profile.LeftEdge = left.Value.X;
            profile.RightEdge = right.Value.X;
            profile.EdgeHeight = (left.Value.Height + right.Value.Height) / 2.0;
        }

        /// <summary>
        /// Wires and fences are narrow: all points within <see cref="LineWidth"/> across. Wires hang at least
        /// <see cref="OverheadHeight"/> up; fences and walls rise at least <see cref="FenceHeight"/>, even with a narrow
        /// cap on top. Narrow low strips such as kerbs and road markings stay surfaces.
        /// </summary>
        private static OmsiSplineLineKind Classify(OmsiSplineProfile profile)
        {
            if (profile.EditorOnly || profile.Strips.Count == 0)
                return OmsiSplineLineKind.None;
            double left = double.MaxValue, right = double.MinValue, lowest = double.MaxValue, highest = double.MinValue;
            foreach (var strip in profile.Strips)
                foreach (var point in strip.Points)
                {
                    left = Math.Min(left, point.X);
                    right = Math.Max(right, point.X);
                    lowest = Math.Min(lowest, point.Height);
                    highest = Math.Max(highest, point.Height);
                }
            if (right - left > LineWidth)
                return OmsiSplineLineKind.None;
            if (lowest >= OverheadHeight)
                return OmsiSplineLineKind.Overhead;
            return highest - lowest >= FenceHeight ? OmsiSplineLineKind.Fence : OmsiSplineLineKind.None;
        }

        /// <summary>Profiles at most this wide across count as wires or fences.</summary>
        public const double LineWidth = 0.6;

        /// <summary>Narrow profiles at least this high count as fences or walls.</summary>
        public const double FenceHeight = 1.0;

        private static bool TryNumbers(List<string> values, out double[] numbers)
        {
            numbers = new double[4];
            if (values.Count < 4)
                return false;
            for (var index = 0; index < 4; index++)
                if (!double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]))
                    return false;
            return true;
        }
    }

    /// <summary>Triangles of one texture of a spline: positions (east, height, north), texture coordinates and indices.</summary>
    public sealed class OmsiSurfaceMesh
    {
        public int TextureIndex { get; internal set; }
        public List<double> Positions { get; } = new List<double>();
        public List<double> Uvs { get; } = new List<double>();
        public List<int> Triangles { get; } = new List<int>();

        public int VertexCount => Positions.Count / 3;
    }

    /// <summary>Extrudes spline profiles along a spline's plan-view curve.</summary>
    public static class OmsiSplineSurface
    {
        /// <summary>Strips narrower than this seen from above, such as kerb faces, are left out.</summary>
        public const double MinimumWidth = 0.02;

        /// <summary>Largest change of heading between two samples of a curve.</summary>
        private const double MaximumStepRadians = 2.0 * Math.PI / 180.0;

        /// <summary>
        /// Builds the surfaces a spline shows from above, one per texture. Positions are in metres relative to the
        /// spline's tile corner: x east, y height (spline height plus profile height), z north. A mirrored spline turns
        /// its cross-section over. With <paramref name="includeUpright"/> strips that stand upright, such as kerb faces,
        /// walls and fences, are built as well, for views from the side.
        /// </summary>
        public static List<OmsiSurfaceMesh> Build(OmsiSplineRecord spline, OmsiSplineProfile profile, bool includeUpright = false)
        {
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var meshes = new Dictionary<int, OmsiSurfaceMesh>();
            var length = spline.LengthMeters;
            if (!(length > 0.0) || double.IsInfinity(length))
                return new List<OmsiSurfaceMesh>();

            var turn = Math.Abs(spline.RadiusMeters) > 0.001 ? length / Math.Abs(spline.RadiusMeters) : 0.0;
            var steps = Math.Max(1, Math.Min(512, (int)Math.Ceiling(turn / MaximumStepRadians)));
            var mirror = spline.IsMirrored ? -1.0 : 1.0;

            foreach (var strip in profile.Strips)
            {
                if (strip.TextureIndex < 0 || strip.TextureIndex >= profile.Textures.Count)
                    continue;
                if (!meshes.TryGetValue(strip.TextureIndex, out var mesh))
                {
                    mesh = new OmsiSurfaceMesh { TextureIndex = strip.TextureIndex };
                    meshes.Add(strip.TextureIndex, mesh);
                }
                for (var point = 0; point + 1 < strip.Points.Count; point++)
                {
                    var left = strip.Points[point];
                    var right = strip.Points[point + 1];
                    if (Math.Abs(right.X - left.X) < MinimumWidth && (!includeUpright || Math.Abs(right.Height - left.Height) < MinimumWidth))
                        continue;
                    AddRibbon(mesh, spline, left, right, mirror, steps);
                }
            }

            var result = new List<OmsiSurfaceMesh>();
            foreach (var mesh in meshes.Values)
                if (mesh.Triangles.Count > 0)
                    result.Add(mesh);
            return result;
        }

        /// <summary>
        /// The terrain holes of a spline seen from above: each piece between two neighbouring hole points swept along the
        /// spline, as triangles relative to the spline's tile corner (x east, y height, z north).
        /// </summary>
        public static OmsiSurfaceMesh BuildTerrainHoles(OmsiSplineRecord spline, OmsiSplineProfile profile)
        {
            var mesh = new OmsiSurfaceMesh();
            var length = spline.LengthMeters;
            if (profile.TerrainHoles.Count == 0 || !(length > 0.0) || double.IsInfinity(length))
                return mesh;
            var turn = Math.Abs(spline.RadiusMeters) > 0.001 ? length / Math.Abs(spline.RadiusMeters) : 0.0;
            var steps = Math.Max(1, Math.Min(512, (int)Math.Ceiling(turn / MaximumStepRadians)));
            var mirror = spline.IsMirrored ? -1.0 : 1.0;
            foreach (var hole in profile.TerrainHoles)
                for (var point = 0; point + 1 < hole.Count; point++)
                    if (Math.Abs(hole[point + 1] - hole[point]) >= MinimumWidth)
                        AddRibbon(mesh, spline, new OmsiProfilePoint { X = hole[point] }, new OmsiProfilePoint { X = hole[point + 1] }, mirror, steps);
            return mesh;
        }

        /// <summary>
        /// The terrain OMSI cuts away under a spline marked with [spline_terrain_align] or [spline_terrain_align_2]: its
        /// contour, the widths its strips cover seen from above. Widths without a strip, such as a grass verge between a
        /// road and its pavement, keep their terrain. Each covered width shrinks by <paramref name="insetMeters"/> on both
        /// sides, so the terrain reaches under its edges; widths left without area are dropped.
        /// </summary>
        public static OmsiSurfaceMesh BuildAlignmentHole(OmsiSplineRecord spline, OmsiSplineProfile profile, double insetMeters = 0.0)
        {
            var mesh = new OmsiSurfaceMesh();
            var length = spline.LengthMeters;
            if (!(length > 0.0) || double.IsInfinity(length))
                return mesh;
            var widths = new List<KeyValuePair<double, double>>();
            foreach (var strip in profile.Strips)
                for (var point = 0; point + 1 < strip.Points.Count; point++)
                {
                    var left = Math.Min(strip.Points[point].X, strip.Points[point + 1].X);
                    var right = Math.Max(strip.Points[point].X, strip.Points[point + 1].X);
                    if (right - left >= MinimumWidth)
                        widths.Add(new KeyValuePair<double, double>(left, right));
                }
            if (widths.Count == 0)
                return mesh;
            widths.Sort((first, second) => first.Key.CompareTo(second.Key));
            var merged = new List<KeyValuePair<double, double>> { widths[0] };
            for (var index = 1; index < widths.Count; index++)
            {
                var last = merged[merged.Count - 1];
                if (widths[index].Key <= last.Value + MinimumWidth)
                    merged[merged.Count - 1] = new KeyValuePair<double, double>(last.Key, Math.Max(last.Value, widths[index].Value));
                else
                    merged.Add(widths[index]);
            }

            var turn = Math.Abs(spline.RadiusMeters) > 0.001 ? length / Math.Abs(spline.RadiusMeters) : 0.0;
            var steps = Math.Max(1, Math.Min(512, (int)Math.Ceiling(turn / MaximumStepRadians)));
            foreach (var width in merged)
                if (width.Value - width.Key - 2.0 * insetMeters >= MinimumWidth)
                    AddRibbon(mesh, spline, new OmsiProfilePoint { X = width.Key + insetMeters }, new OmsiProfilePoint { X = width.Value - insetMeters },
                        spline.IsMirrored ? -1.0 : 1.0, steps);
            return mesh;
        }

        /// <summary>One piece of the cross-section swept along the spline as a band of quads.</summary>
        private static void AddRibbon(OmsiSurfaceMesh mesh, OmsiSplineRecord spline, OmsiProfilePoint left, OmsiProfilePoint right,
            double mirror, int steps)
        {
            var first = mesh.VertexCount;
            for (var step = 0; step <= steps; step++)
            {
                var distance = spline.LengthMeters * step / steps;
                var height = spline.Height + (spline.EndHeight - spline.Height) * step / steps;
                OmsiMapGeometry.GetLocalFrameAt(spline, distance, out var position, out var heading);
                // The right-hand side of the travel direction: heading 0 runs north, so right is east.
                var rightX = Math.Cos(heading);
                var rightY = -Math.Sin(heading);
                foreach (var point in new[] { left, right })
                {
                    var offset = point.X * mirror;
                    mesh.Positions.Add(position.X + rightX * offset);
                    mesh.Positions.Add(height + point.Height);
                    mesh.Positions.Add(position.Y + rightY * offset);
                    mesh.Uvs.Add(point.U);
                    mesh.Uvs.Add(distance * point.VPerMeter);
                }
            }
            for (var step = 0; step < steps; step++)
            {
                var a = first + step * 2;
                mesh.Triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
        }
    }

    /// <summary>
    /// Reads and caches the spline profiles of one OMSI installation by asset path, as tile files name them.
    /// </summary>
    public sealed class OmsiSplineProfileCatalog
    {
        private readonly string installationRoot;
        private readonly Dictionary<string, OmsiSplineProfile> profiles = new Dictionary<string, OmsiSplineProfile>(StringComparer.OrdinalIgnoreCase);

        public OmsiSplineProfileCatalog(string installationRoot)
        {
            this.installationRoot = System.IO.Path.GetFullPath(installationRoot);
        }

        /// <summary>Problems met while reading, one per file.</summary>
        public List<string> Problems { get; } = new List<string>();

        /// <summary>The profile, or null when the file is missing or unreadable; the reason goes to <see cref="Problems"/>.</summary>
        public OmsiSplineProfile Get(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
            if (profiles.TryGetValue(assetPath, out var cached))
                return cached;

            OmsiSplineProfile profile = null;
            try
            {
                var path = OmsiRelativePath.Resolve(installationRoot, assetPath);
                if (File.Exists(path))
                    profile = OmsiSplineProfile.Parse(File.ReadAllBytes(path), assetPath);
                else
                    Problems.Add(assetPath + " not found");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Problems.Add(assetPath + ": " + exception.Message);
            }
            profiles.Add(assetPath, profile);
            return profile;
        }
    }
}
