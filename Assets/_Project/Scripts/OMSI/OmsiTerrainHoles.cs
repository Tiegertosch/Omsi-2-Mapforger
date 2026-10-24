using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Where the terrain of one tile is cut away, seen from above: <see cref="Resolution"/> × <see cref="Resolution"/>
    /// pixels, row 0 at the south edge and column 0 at the west edge, 255 inside a hole and 0 elsewhere.
    /// </summary>
    public sealed class OmsiTerrainHoleMask
    {
        public const int Resolution = 1024;

        public OmsiTerrainHoleMask(int tileX, int tileY)
        {
            TileX = tileX;
            TileY = tileY;
        }

        public int TileX { get; }
        public int TileY { get; }
        public byte[] Pixels { get; } = new byte[Resolution * Resolution];

        /// <summary>How many pixels lie in a hole.</summary>
        public int HolePixelCount { get; private set; }

        /// <summary>
        /// Shrinks the holes by <paramref name="meters"/> on every side, so the ground still reaches under the edges of
        /// the meshes around a hole; hole meshes tend to be a little larger than the road surfaces that cover them.
        /// </summary>
        public void Shrink(double meters, double tileSize)
        {
            var radius = (int)Math.Ceiling(meters * Resolution / tileSize);
            if (radius <= 0 || HolePixelCount == 0)
                return;
            // A square minimum filter, along rows and then along columns.
            var rows = new byte[Pixels.Length];
            for (var row = 0; row < Resolution; row++)
                for (var column = 0; column < Resolution; column++)
                    rows[row * Resolution + column] = Minimum(Pixels, row * Resolution, 1, column, radius);
            HolePixelCount = 0;
            for (var column = 0; column < Resolution; column++)
                for (var row = 0; row < Resolution; row++)
                {
                    var value = Minimum(rows, column, Resolution, row, radius);
                    Pixels[row * Resolution + column] = value;
                    if (value != 0)
                        HolePixelCount++;
                }
        }

        /// <summary>The smallest value within <paramref name="radius"/> of a position along a row or column; outside counts as hole.</summary>
        private static byte Minimum(byte[] values, int start, int stride, int position, int radius)
        {
            var from = Math.Max(0, position - radius);
            var to = Math.Min(Resolution - 1, position + radius);
            for (var index = from; index <= to; index++)
                if (values[start + index * stride] == 0)
                    return 0;
            return 255;
        }

        /// <summary>Marks the pixels whose centre lies in a triangle given in tile-local metres (east, north).</summary>
        public void AddTriangle(double ax, double ay, double bx, double by, double cx, double cy, double tileSize)
        {
            var scale = Resolution / tileSize;
            ax *= scale;
            ay *= scale;
            bx *= scale;
            by *= scale;
            cx *= scale;
            cy *= scale;
            var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Math.Abs(area) < 1e-9)
                return;
            var minimumX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
            var maximumX = Math.Min(Resolution - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            var minimumY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
            var maximumY = Math.Min(Resolution - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            var sign = area > 0 ? 1.0 : -1.0;
            for (var row = minimumY; row <= maximumY; row++)
            {
                var y = row + 0.5;
                for (var column = minimumX; column <= maximumX; column++)
                {
                    var x = column + 0.5;
                    var first = ((bx - ax) * (y - ay) - (by - ay) * (x - ax)) * sign;
                    var second = ((cx - bx) * (y - by) - (cy - by) * (x - bx)) * sign;
                    var third = ((ax - cx) * (y - cy) - (ay - cy) * (x - cx)) * sign;
                    if (first < 0 || second < 0 || third < 0)
                        continue;
                    var index = row * Resolution + column;
                    if (Pixels[index] != 0)
                        continue;
                    Pixels[index] = 255;
                    HolePixelCount++;
                }
            }
        }
    }

    /// <summary>An object placement that may cut the terrain: its tile, tile-local position and orientation.</summary>
    public struct OmsiHolePlacement
    {
        public int TileX;
        public int TileY;
        public string AssetPath;
        public double LocalX;
        public double LocalY;
        public double HeadingDegrees;
        public double PitchDegrees;
        public double BankDegrees;
    }

    /// <summary>
    /// The terrain holes of a map, as OMSI cuts them: under objects with a <c>[terrainhole]</c> mesh, such as crossings
    /// whose road surface lies below the ground around it, and along splines whose profile has a
    /// <c>[terrainholeprofile]</c>, such as rail cuttings. Holes are drawn into every tile they reach.
    /// </summary>
    public sealed class OmsiTerrainHoleCatalog
    {
        private readonly string installationRoot;
        private readonly Dictionary<string, float[]> objectHoles = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        public OmsiTerrainHoleCatalog(string installationRoot)
        {
            this.installationRoot = Path.GetFullPath(installationRoot);
        }

        public List<string> Problems { get; } = new List<string>();

        /// <summary>
        /// The triangles of an object's terrain holes in its frame (x right, y up, z forward), three coordinates per
        /// corner, or null when the object has none or its file cannot be read.
        /// </summary>
        public float[] ObjectHole(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
            if (objectHoles.TryGetValue(assetPath, out var cached))
                return cached;
            float[] result = null;
            try
            {
                var scoPath = OmsiRelativePath.Resolve(installationRoot, assetPath);
                if (File.Exists(scoPath))
                {
                    var sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(scoPath));
                    var corners = new List<float>();
                    foreach (var meshName in sceneryObject.TerrainHoleMeshes)
                        AddMesh(corners, Path.Combine(Path.GetDirectoryName(assetPath) ?? string.Empty, "model", meshName), assetPath);
                    if (corners.Count > 0)
                        result = corners.ToArray();
                }
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Problems.Add(assetPath + ": " + exception.Message);
            }
            objectHoles[assetPath] = result;
            return result;
        }

        private void AddMesh(List<float> corners, string meshPath, string assetPath)
        {
            var fullPath = OmsiRelativePath.Resolve(installationRoot, meshPath);
            if (!File.Exists(fullPath))
            {
                Problems.Add("Terrain hole " + meshPath + " not found (" + assetPath + ")");
                return;
            }
            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length >= 2 && bytes[0] == 0x84 && bytes[1] == 0x19)
            {
                var mesh = OmsiO3dMesh.Parse(bytes);
                foreach (var index in mesh.TriangleIndices)
                {
                    var vertex = mesh.Vertices[index];
                    corners.Add(vertex.X);
                    corners.Add(vertex.Y);
                    corners.Add(vertex.Z);
                }
            }
            else
            {
                var mesh = OmsiXMesh.Parse(bytes);
                foreach (var index in mesh.TriangleIndices)
                    corners.AddRange(mesh.Positions[index]);
            }
        }

        /// <summary>
        /// Draws the holes of the given placements and splines into masks of the available tiles; tiles without a hole
        /// get no mask.
        /// </summary>
        /// <param name="availableTiles">Tiles that have terrain to cut, by <see cref="TileKey"/>.</param>
        public Dictionary<long, OmsiTerrainHoleMask> Build(IEnumerable<OmsiHolePlacement> placements,
            IEnumerable<KeyValuePair<long, OmsiSplineRecord>> splines, OmsiSplineProfileCatalog profiles, ISet<long> availableTiles,
            double tileSize)
        {
            var masks = new Dictionary<long, OmsiTerrainHoleMask>();
            foreach (var placement in placements)
            {
                var corners = ObjectHole(placement.AssetPath);
                if (corners == null)
                    continue;
                var transformed = new double[corners.Length / 3 * 2];
                var originX = placement.TileX * tileSize + placement.LocalX;
                var originY = placement.TileY * tileSize + placement.LocalY;
                for (var corner = 0; corner * 3 + 2 < corners.Length; corner++)
                {
                    Rotate(corners[corner * 3], corners[corner * 3 + 1], corners[corner * 3 + 2], placement.HeadingDegrees, placement.PitchDegrees,
                        placement.BankDegrees, out var east, out var north);
                    transformed[corner * 2] = originX + east;
                    transformed[corner * 2 + 1] = originY + north;
                }
                AddWorldTriangles(masks, transformed, availableTiles, tileSize);
            }

            foreach (var pair in splines)
            {
                var profile = profiles.Get(pair.Value.AssetPath);
                if (profile != null && profile.TerrainHoles.Count > 0)
                    AddSplineTriangles(masks, pair.Key, OmsiSplineSurface.BuildTerrainHoles(pair.Value, profile), availableTiles, tileSize);
            }
            foreach (var mask in masks.Values)
                mask.Shrink(EdgeAllowanceMeters, tileSize);

            // Without a hole profile a terrain alignment cuts the contour of the cross-section, as OMSI does. It is cut
            // after shrinking, inset by only AlignmentOverlapMeters, so narrow gaps such as a central reservation stay
            // closed and the terrain, which lies at kerb height there, does not rise through the pavement on a slope.
            foreach (var pair in splines)
            {
                var profile = profiles.Get(pair.Value.AssetPath);
                if (profile != null && profile.TerrainHoles.Count == 0 && pair.Value.AlignsTerrain)
                    AddSplineTriangles(masks, pair.Key, OmsiSplineSurface.BuildAlignmentHole(pair.Value, profile, AlignmentOverlapMeters), availableTiles,
                        tileSize);
            }
            return masks;
        }

        private static void AddSplineTriangles(Dictionary<long, OmsiTerrainHoleMask> masks, long tileKey, OmsiSurfaceMesh mesh, ISet<long> availableTiles,
            double tileSize)
        {
            var tileX = (int)(tileKey >> 32);
            var tileY = (int)(uint)tileKey;
            var transformed = new double[mesh.Triangles.Count * 2];
            for (var corner = 0; corner < mesh.Triangles.Count; corner++)
            {
                var vertex = mesh.Triangles[corner];
                transformed[corner * 2] = tileX * tileSize + mesh.Positions[vertex * 3];
                transformed[corner * 2 + 1] = tileY * tileSize + mesh.Positions[vertex * 3 + 2];
            }
            AddWorldTriangles(masks, transformed, availableTiles, tileSize);
        }

        /// <summary>Holes are cut this much smaller than their meshes, so no gap opens at the edges of crossings.</summary>
        public const double EdgeAllowanceMeters = 0.75;

        /// <summary>How far the terrain reaches under the edges of a spline that terrain alignment cuts out.</summary>
        public const double AlignmentOverlapMeters = 0.3;

        public static long TileKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        /// <summary>
        /// OMSI's object rotation, bank about the forward axis, then pitch about the right axis, then heading about the
        /// vertical, projected onto the ground: x east and z north of the rotated point.
        /// </summary>
        public static void Rotate(double x, double y, double z, double headingDegrees, double pitchDegrees, double bankDegrees,
            out double east, out double north)
        {
            var bank = bankDegrees * Math.PI / 180.0;
            var pitch = pitchDegrees * Math.PI / 180.0;
            var heading = headingDegrees * Math.PI / 180.0;
            var x1 = x * Math.Cos(bank) - y * Math.Sin(bank);
            var y1 = x * Math.Sin(bank) + y * Math.Cos(bank);
            var z2 = y1 * Math.Sin(pitch) + z * Math.Cos(pitch);
            east = x1 * Math.Cos(heading) + z2 * Math.Sin(heading);
            north = -x1 * Math.Sin(heading) + z2 * Math.Cos(heading);
        }

        /// <summary>Draws triangles in world metres (east, north pairs) into every available tile they overlap.</summary>
        private static void AddWorldTriangles(Dictionary<long, OmsiTerrainHoleMask> masks, double[] points, ISet<long> availableTiles,
            double tileSize)
        {
            for (var start = 0; start + 5 < points.Length; start += 6)
            {
                var minimumX = Math.Min(points[start], Math.Min(points[start + 2], points[start + 4]));
                var maximumX = Math.Max(points[start], Math.Max(points[start + 2], points[start + 4]));
                var minimumY = Math.Min(points[start + 1], Math.Min(points[start + 3], points[start + 5]));
                var maximumY = Math.Max(points[start + 1], Math.Max(points[start + 3], points[start + 5]));
                for (var tileX = (int)Math.Floor(minimumX / tileSize); tileX <= (int)Math.Floor(maximumX / tileSize); tileX++)
                {
                    for (var tileY = (int)Math.Floor(minimumY / tileSize); tileY <= (int)Math.Floor(maximumY / tileSize); tileY++)
                    {
                        var key = TileKey(tileX, tileY);
                        if (!availableTiles.Contains(key))
                            continue;
                        if (!masks.TryGetValue(key, out var mask))
                            masks[key] = mask = new OmsiTerrainHoleMask(tileX, tileY);
                        var cornerX = tileX * tileSize;
                        var cornerY = tileY * tileSize;
                        mask.AddTriangle(points[start] - cornerX, points[start + 1] - cornerY, points[start + 2] - cornerX,
                            points[start + 3] - cornerY, points[start + 4] - cornerX, points[start + 5] - cornerY, tileSize);
                    }
                }
            }
        }
    }
}
