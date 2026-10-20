using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A mesh entry of a scenery object; editor-only meshes are helpers shown only in the OMSI editor.</summary>
    public sealed class OmsiSceneryMesh
    {
        public string FileName { get; internal set; }
        public bool EditorOnly { get; internal set; }
    }

    /// <summary>Tree parameters from [tree]: texture, height range and width-to-height ratio range.</summary>
    public sealed class OmsiTreeShape
    {
        public string Texture { get; internal set; }
        public double MinHeight { get; internal set; }
        public double MaxHeight { get; internal set; }
        public double MinWidthRatio { get; internal set; }
        public double MaxWidthRatio { get; internal set; }
    }

    /// <summary>A traffic path of a scenery object, in the object frame (x right, y forward).</summary>
    public sealed class OmsiObjectPath
    {
        public double X { get; internal set; }
        public double Y { get; internal set; }
        public double HeadingDegrees { get; internal set; }
        public double RadiusMeters { get; internal set; }
        public double LengthMeters { get; internal set; }
        public double WidthMeters { get; internal set; }
    }

    /// <summary>A spline connection point of a crossing or switch, in the object frame (x right, y forward).</summary>
    public sealed class OmsiSplineHelper
    {
        public string SplinePath { get; internal set; }
        public double X { get; internal set; }
        public double Height { get; internal set; }
        public double Y { get; internal set; }
        public double HeadingDegrees { get; internal set; }
    }

    /// <summary>The parts of a scenery object (.sco) that matter for its footprint.</summary>
    public sealed class OmsiSceneryObject
    {
        public List<OmsiSceneryMesh> Meshes { get; } = new List<OmsiSceneryMesh>();
        public List<string> CollisionMeshes { get; } = new List<string>();

        /// <summary>[boundingbox]: size x, y, z, then centre x, y, z in the object frame (z up), or null.</summary>
        public double[] BoundingBox { get; internal set; }

        public OmsiTreeShape Tree { get; internal set; }
        public List<OmsiObjectPath> Paths { get; } = new List<OmsiObjectPath>();
        public bool HasAbsoluteHeight { get; internal set; }
        public bool HasSplineHelper => SplineHelpers.Count > 0;

        /// <summary>[splinehelper]: spline file, then x, height, y and heading, the order spline records use.</summary>
        public List<OmsiSplineHelper> SplineHelpers { get; } = new List<OmsiSplineHelper>();

        public static OmsiSceneryObject Parse(byte[] bytes)
        {
            var file = OmsiRecordFile.Parse(bytes, null);
            var result = new OmsiSceneryObject();
            var nextMeshIsEditorOnly = false;
            foreach (var record in file.Records)
            {
                var values = record.AllValues();
                switch (record.Name.ToLowerInvariant())
                {
                    case "onlyeditor":
                        nextMeshIsEditorOnly = true;
                        break;
                    case "mesh":
                        if (values.Count > 0 && values[0].Length > 0)
                            result.Meshes.Add(new OmsiSceneryMesh { FileName = values[0], EditorOnly = nextMeshIsEditorOnly });
                        nextMeshIsEditorOnly = false;
                        break;
                    case "collision_mesh":
                    case "collisionmesh":
                        if (values.Count > 0 && values[0].Length > 0)
                            result.CollisionMeshes.Add(values[0]);
                        break;
                    case "boundingbox":
                        if (TryNumbers(values, 6, out var box))
                            result.BoundingBox = box;
                        break;
                    case "tree":
                        if (values.Count >= 5 && TryNumbers(values.GetRange(1, 4), 4, out var tree))
                            result.Tree = new OmsiTreeShape
                            {
                                Texture = values[0], MinHeight = tree[0], MaxHeight = tree[1], MinWidthRatio = tree[2], MaxWidthRatio = tree[3]
                            };
                        break;
                    case "path":
                    case "path_2":
                        if (TryNumbers(values, 10, out var path))
                            result.Paths.Add(new OmsiObjectPath
                            {
                                X = path[0], Y = path[1], HeadingDegrees = path[3], RadiusMeters = path[4], LengthMeters = path[5], WidthMeters = path[9]
                            });
                        break;
                    case "absheight":
                        result.HasAbsoluteHeight = true;
                        break;
                    case "splinehelper":
                        if (values.Count >= 5 && TryNumbers(values.GetRange(1, 4), 4, out var helper))
                            result.SplineHelpers.Add(new OmsiSplineHelper
                            {
                                SplinePath = values[0], X = helper[0], Height = helper[1], Y = helper[2], HeadingDegrees = helper[3]
                            });
                        break;
                }
            }
            return result;
        }

        private static bool TryNumbers(List<string> values, int count, out double[] numbers)
        {
            numbers = null;
            if (values.Count < count)
                return false;
            var result = new double[count];
            for (var index = 0; index < count; index++)
                if (!double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]))
                    return false;
            numbers = result;
            return true;
        }
    }

    public enum OmsiOutlineSource
    {
        None,
        Mesh,
        Tree,
        BoundingBox,
        Paths
    }

    /// <summary>Footprint of a scenery object seen from above, in the object frame: x right, y forward, metres.</summary>
    public sealed class OmsiObjectOutline
    {
        public OmsiOutlineSource Source { get; internal set; }

        /// <summary>Convex polygon, counter-clockwise; empty when no footprint could be derived.</summary>
        public OmsiMapPoint[] Points { get; internal set; } = new OmsiMapPoint[0];

        /// <summary>Why the outline is missing or incomplete, for diagnostics; null when everything was read.</summary>
        public string Problem { get; internal set; }
    }

    /// <summary>
    /// Builds and caches outlines for scenery objects of one OMSI installation. Asset paths are relative to the
    /// installation folder, as tile files store them. Meshes shared by several objects are read once.
    /// </summary>
    public sealed class OmsiObjectOutlineCatalog
    {
        private readonly string installationRoot;
        private readonly Dictionary<string, OmsiObjectOutline> outlines = new Dictionary<string, OmsiObjectOutline>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<OmsiMapPoint>> meshPoints = new Dictionary<string, List<OmsiMapPoint>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> meshProblems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public OmsiObjectOutlineCatalog(string installationRoot)
        {
            this.installationRoot = Path.GetFullPath(installationRoot);
        }

        /// <summary>The OMSI installation that contains a map folder (<c>OMSI 2\maps\Name</c>).</summary>
        public static string InstallationRootOfMap(string mapRootPath)
        {
            return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(mapRootPath)));
        }

        public OmsiObjectOutline Get(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return new OmsiObjectOutline { Problem = "No object file." };
            if (!outlines.TryGetValue(assetPath, out var outline))
            {
                outline = Build(assetPath);
                outlines.Add(assetPath, outline);
            }
            return outline;
        }

        private OmsiObjectOutline Build(string assetPath)
        {
            string scoPath;
            try
            {
                scoPath = OmsiRelativePath.Resolve(installationRoot, assetPath);
            }
            catch (ArgumentException exception)
            {
                return new OmsiObjectOutline { Problem = exception.Message };
            }
            if (!File.Exists(scoPath))
                return new OmsiObjectOutline { Problem = "Object file not found: " + assetPath };

            OmsiSceneryObject sceneryObject;
            try
            {
                sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(scoPath));
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
            {
                return new OmsiObjectOutline { Problem = exception.Message };
            }

            if (sceneryObject.Tree != null)
                return TreeOutline(sceneryObject.Tree);

            var modelFolder = Path.Combine(Path.GetDirectoryName(scoPath), "model");
            var meshes = sceneryObject.Meshes.FindAll(mesh => !mesh.EditorOnly);
            if (meshes.Count == 0)
                meshes = sceneryObject.Meshes;
            var points = new List<OmsiMapPoint>();
            string problem = null;
            foreach (var mesh in meshes)
            {
                var meshPath = Path.Combine(modelFolder, mesh.FileName.Replace('\\', Path.DirectorySeparatorChar));
                var meshResult = MeshPoints(meshPath, out var meshProblem);
                if (meshResult != null)
                    points.AddRange(meshResult);
                else
                    problem = meshProblem;
            }

            var hull = OmsiConvexHull.Compute(points);
            if (hull.Length >= 2)
                return new OmsiObjectOutline { Source = OmsiOutlineSource.Mesh, Points = hull, Problem = problem };

            if (sceneryObject.BoundingBox != null)
                return BoxOutline(sceneryObject.BoundingBox, problem);
            if (sceneryObject.Paths.Count > 0)
                return PathOutline(sceneryObject.Paths, problem);
            return new OmsiObjectOutline { Problem = problem ?? "The object has no mesh, bounding box or path." };
        }

        /// <summary>Horizontal mesh points in the object frame: x = mesh X, y = mesh Z (Direct3D Z points forward).</summary>
        private List<OmsiMapPoint> MeshPoints(string meshPath, out string problem)
        {
            problem = null;
            if (meshPoints.TryGetValue(meshPath, out var cached))
                return cached;
            if (meshProblems.TryGetValue(meshPath, out problem))
                return null;

            try
            {
                if (!File.Exists(meshPath))
                    throw new FileNotFoundException("Mesh not found: " + Path.GetFileName(meshPath));
                var bytes = File.ReadAllBytes(meshPath);
                var result = new List<OmsiMapPoint>();
                if (bytes.Length >= 2 && bytes[0] == 0x84 && bytes[1] == 0x19)
                {
                    var mesh = OmsiO3dMesh.Parse(bytes);
                    foreach (var index in UsedVertices(mesh.TriangleIndices, mesh.Vertices.Count))
                        result.Add(new OmsiMapPoint(mesh.Vertices[index].X, mesh.Vertices[index].Z));
                }
                else
                {
                    var mesh = OmsiXMesh.Parse(bytes);
                    foreach (var index in UsedVertices(mesh.TriangleIndices, mesh.Positions.Count))
                        result.Add(new OmsiMapPoint(mesh.Positions[index][0], mesh.Positions[index][2]));
                }
                meshPoints.Add(meshPath, result);
                return result;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
            {
                problem = exception.Message;
                meshProblems.Add(meshPath, problem);
                return null;
            }
        }

        /// <summary>Vertices that belong to at least one triangle; stray vertices would distort the footprint.</summary>
        private static IEnumerable<int> UsedVertices(List<int> triangleIndices, int vertexCount)
        {
            var used = new bool[vertexCount];
            foreach (var index in triangleIndices)
                if (index >= 0 && index < vertexCount)
                    used[index] = true;
            for (var index = 0; index < vertexCount; index++)
                if (used[index])
                    yield return index;
        }

        /// <summary>A circle with the mean crown width: mean height times mean width-to-height ratio.</summary>
        private static OmsiObjectOutline TreeOutline(OmsiTreeShape tree)
        {
            var radius = 0.25 * (tree.MinHeight + tree.MaxHeight) * 0.5 * (tree.MinWidthRatio + tree.MaxWidthRatio);
            const int corners = 12;
            var points = new OmsiMapPoint[corners];
            for (var corner = 0; corner < corners; corner++)
            {
                var angle = 2.0 * Math.PI * corner / corners;
                points[corner] = new OmsiMapPoint(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }
            return new OmsiObjectOutline { Source = OmsiOutlineSource.Tree, Points = points };
        }

        private static OmsiObjectOutline BoxOutline(double[] box, string problem)
        {
            double halfX = box[0] * 0.5, halfY = box[1] * 0.5, centerX = box[3], centerY = box[4];
            return new OmsiObjectOutline
            {
                Source = OmsiOutlineSource.BoundingBox,
                Problem = problem,
                Points = new[]
                {
                    new OmsiMapPoint(centerX - halfX, centerY - halfY), new OmsiMapPoint(centerX + halfX, centerY - halfY),
                    new OmsiMapPoint(centerX + halfX, centerY + halfY), new OmsiMapPoint(centerX - halfX, centerY + halfY)
                }
            };
        }

        /// <summary>The hull of all paths, each widened to its lane width.</summary>
        private static OmsiObjectOutline PathOutline(List<OmsiObjectPath> paths, string problem)
        {
            var points = new List<OmsiMapPoint>();
            foreach (var path in paths)
            {
                var spline = new OmsiSplineRecord
                {
                    LocalX = path.X, LocalY = path.Y, HeadingDegrees = path.HeadingDegrees,
                    LengthMeters = path.LengthMeters, RadiusMeters = path.RadiusMeters
                };
                var centerLine = OmsiMapGeometry.BuildSplinePolyline(new OmsiTileSummary(), spline, 1e9);
                var half = Math.Max(path.WidthMeters, 0.1) * 0.5;
                for (var index = 0; index < centerLine.Length; index++)
                {
                    var next = centerLine[Math.Min(index + 1, centerLine.Length - 1)];
                    var previous = centerLine[Math.Max(index - 1, 0)];
                    var dx = next.X - previous.X;
                    var dy = next.Y - previous.Y;
                    var length = Math.Sqrt(dx * dx + dy * dy);
                    if (length < 1e-9)
                        continue;
                    points.Add(new OmsiMapPoint(centerLine[index].X - dy / length * half, centerLine[index].Y + dx / length * half));
                    points.Add(new OmsiMapPoint(centerLine[index].X + dy / length * half, centerLine[index].Y - dx / length * half));
                }
            }
            return new OmsiObjectOutline { Source = OmsiOutlineSource.Paths, Points = OmsiConvexHull.Compute(points), Problem = problem };
        }
    }

    /// <summary>Convex hull by Andrew's monotone chain.</summary>
    public static class OmsiConvexHull
    {
        public static OmsiMapPoint[] Compute(List<OmsiMapPoint> points)
        {
            if (points == null || points.Count == 0)
                return new OmsiMapPoint[0];

            var sorted = new List<OmsiMapPoint>(points);
            sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            var unique = new List<OmsiMapPoint> { sorted[0] };
            for (var index = 1; index < sorted.Count; index++)
            {
                var last = unique[unique.Count - 1];
                if (Math.Abs(sorted[index].X - last.X) > 1e-6 || Math.Abs(sorted[index].Y - last.Y) > 1e-6)
                    unique.Add(sorted[index]);
            }
            if (unique.Count <= 2)
                return unique.ToArray();

            var hull = new OmsiMapPoint[unique.Count * 2];
            var size = 0;
            for (var index = 0; index < unique.Count; index++)
            {
                while (size >= 2 && Cross(hull[size - 2], hull[size - 1], unique[index]) <= 0)
                    size--;
                hull[size++] = unique[index];
            }
            for (int index = unique.Count - 2, lower = size + 1; index >= 0; index--)
            {
                while (size >= lower && Cross(hull[size - 2], hull[size - 1], unique[index]) <= 0)
                    size--;
                hull[size++] = unique[index];
            }

            var result = new OmsiMapPoint[size - 1];
            Array.Copy(hull, result, size - 1);
            return result;
        }

        private static double Cross(OmsiMapPoint origin, OmsiMapPoint a, OmsiMapPoint b)
        {
            return (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);
        }
    }
}
