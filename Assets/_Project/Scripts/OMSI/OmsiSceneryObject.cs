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

        /// <summary>Index of the [LOD] level the mesh belongs to in file order, or -1 when it comes before any [LOD].</summary>
        public int LodLevel { get; internal set; } = -1;

        /// <summary>The variable of a [visible] condition, or null when the mesh is always shown.</summary>
        public string VisibleVariable { get; internal set; }

        /// <summary>The mesh is shown while <see cref="VisibleVariable"/> has this value.</summary>
        public double VisibleValue { get; internal set; }

        /// <summary>[matl] settings for materials of this mesh, chosen by texture name and index.</summary>
        public List<OmsiSceneryMaterial> Materials { get; } = new List<OmsiSceneryMaterial>();
    }

    /// <summary>A [matl] entry: the material of the mesh with this texture, and its alpha mode from [matl_alpha].</summary>
    public sealed class OmsiSceneryMaterial
    {
        public string Texture { get; internal set; }

        /// <summary>Which material with that texture is meant, counting from 0.</summary>
        public int Index { get; internal set; }

        /// <summary>0 opaque; 1 the alpha channel cuts the texture; 2 it blends.</summary>
        public int AlphaMode { get; internal set; }

        /// <summary>
        /// [matl_transmap]: a texture whose alpha gives the opacity instead of the main texture's alpha, which then
        /// holds the reflection strength for [matl_envmap]. Null when the main texture's alpha applies.
        /// </summary>
        public string TransparencyTexture { get; internal set; }

        /// <summary>
        /// [useTextTexture] or [useScriptTexture]: OMSI replaces the texture at run time with one a script fills, such
        /// as a display text; the named file is only a placeholder.
        /// </summary>
        public bool HasGeneratedTexture { get; internal set; }
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

    /// <summary>One step of an attachment point's transform, applied in file order.</summary>
    public sealed class OmsiAttachStep
    {
        /// <summary>"attach_trans", "attach_rot_x", "attach_rot_y" or "attach_rot_z".</summary>
        public string Kind { get; internal set; }

        /// <summary>For a translation: x right, y forward, z up, in metres. For a rotation: the angle in degrees in X.</summary>
        public double X { get; internal set; }

        public double Y { get; internal set; }
        public double Z { get; internal set; }
    }

    /// <summary>A [new_attachment] point where [attachObj] objects hang, in the object frame.</summary>
    public sealed class OmsiAttachPoint
    {
        public List<OmsiAttachStep> Steps { get; } = new List<OmsiAttachStep>();
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

        /// <summary>[new_attachment] points in file order; [attachObj] names them by index.</summary>
        public List<OmsiAttachPoint> AttachPoints { get; } = new List<OmsiAttachPoint>();

        public static OmsiSceneryObject Parse(byte[] bytes)
        {
            var file = OmsiRecordFile.Parse(bytes, null);
            var result = new OmsiSceneryObject();
            var nextMeshIsEditorOnly = false;
            var lodLevels = 0;
            OmsiSceneryMesh lastMesh = null;
            OmsiSceneryMaterial lastMaterial = null;
            foreach (var record in file.Records)
            {
                var values = record.AllValues();
                if (string.Equals(record.Name, "new_attachment", StringComparison.OrdinalIgnoreCase))
                    result.AttachPoints.Add(new OmsiAttachPoint());
                // Its sub-commands are plain lines after the block.
                if (result.AttachPoints.Count > 0)
                    ReadAttachSteps(values, result.AttachPoints[result.AttachPoints.Count - 1]);
                switch (record.Name.ToLowerInvariant())
                {
                    case "onlyeditor":
                        nextMeshIsEditorOnly = true;
                        break;
                    case "lod":
                        lodLevels++;
                        break;
                    case "mesh":
                        if (values.Count > 0 && values[0].Length > 0)
                        {
                            lastMesh = new OmsiSceneryMesh { FileName = values[0], EditorOnly = nextMeshIsEditorOnly, LodLevel = lodLevels - 1 };
                            result.Meshes.Add(lastMesh);
                        }
                        lastMaterial = null;
                        nextMeshIsEditorOnly = false;
                        break;
                    case "visible":
                        if (lastMesh != null && values.Count >= 2 &&
                            double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var visibleValue))
                        {
                            lastMesh.VisibleVariable = values[0];
                            lastMesh.VisibleValue = visibleValue;
                        }
                        break;
                    case "matl":
                        lastMaterial = null;
                        if (lastMesh != null && values.Count >= 2 && int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var materialIndex))
                        {
                            lastMaterial = new OmsiSceneryMaterial { Texture = values[0], Index = materialIndex };
                            lastMesh.Materials.Add(lastMaterial);
                        }
                        break;
                    case "matl_alpha":
                        if (lastMaterial != null && values.Count > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var alphaMode))
                            lastMaterial.AlphaMode = alphaMode;
                        break;
                    case "matl_transmap":
                        if (lastMaterial != null && values.Count > 0 && values[0].Length > 0)
                            lastMaterial.TransparencyTexture = values[0];
                        break;
                    case "usetexttexture":
                    case "usescripttexture":
                        if (lastMaterial != null)
                            lastMaterial.HasGeneratedTexture = true;
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

        private static void ReadAttachSteps(List<string> values, OmsiAttachPoint point)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var kind = values[index].ToLowerInvariant();
                var count = kind == "attach_trans" ? 3 : kind == "attach_rot_x" || kind == "attach_rot_y" || kind == "attach_rot_z" ? 1 : 0;
                if (count == 0 || index + count >= values.Count || !TryNumbers(values.GetRange(index + 1, count), count, out var numbers))
                    continue;
                point.Steps.Add(new OmsiAttachStep { Kind = kind, X = numbers[0], Y = count == 3 ? numbers[1] : 0, Z = count == 3 ? numbers[2] : 0 });
                index += count;
            }
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

        /// <summary>Convex hull of the footprint, counter-clockwise; empty when no footprint could be derived.</summary>
        public OmsiMapPoint[] Points { get; internal set; } = new OmsiMapPoint[0];

        /// <summary>
        /// The footprint itself: outer contours counter-clockwise, holes such as courtyards clockwise. Trees and
        /// bounding boxes have their single polygon here.
        /// </summary>
        public IReadOnlyList<OmsiMapPoint[]> Contours { get; internal set; } = new OmsiMapPoint[0][];

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
        private readonly Dictionary<string, List<OmsiMapPoint>> meshTriangles = new Dictionary<string, List<OmsiMapPoint>>(StringComparer.OrdinalIgnoreCase);
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
            var corners = new List<OmsiMapPoint>();
            string problem = null;
            foreach (var mesh in meshes)
            {
                var meshPath = Path.Combine(modelFolder, mesh.FileName.Replace('\\', Path.DirectorySeparatorChar));
                var meshResult = MeshTriangles(meshPath, out var meshProblem);
                if (meshResult != null)
                    corners.AddRange(meshResult);
                else
                    problem = meshProblem;
            }

            var hull = OmsiConvexHull.Compute(corners);
            if (hull.Length >= 2)
                return new OmsiObjectOutline { Source = OmsiOutlineSource.Mesh, Points = hull, Contours = TraceOrHull(corners, hull), Problem = problem };

            if (sceneryObject.BoundingBox != null)
                return BoxOutline(sceneryObject.BoundingBox, problem);
            if (sceneryObject.Paths.Count > 0)
                return PathOutline(sceneryObject.Paths, problem);
            return new OmsiObjectOutline { Problem = problem ?? "The object has no mesh, bounding box or path." };
        }

        /// <summary>The footprint contours, or the hull when the triangles cover no area at all.</summary>
        private static IReadOnlyList<OmsiMapPoint[]> TraceOrHull(List<OmsiMapPoint> corners, OmsiMapPoint[] hull)
        {
            var contours = OmsiFootprintTracer.Trace(corners);
            return contours.Count > 0 ? (IReadOnlyList<OmsiMapPoint[]>)contours : new[] { hull };
        }

        /// <summary>
        /// Triangles seen from above, three corners each, in the object frame: x = mesh X, y = mesh Z (Direct3D Z
        /// points forward).
        /// </summary>
        private List<OmsiMapPoint> MeshTriangles(string meshPath, out string problem)
        {
            problem = null;
            if (meshTriangles.TryGetValue(meshPath, out var cached))
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
                    foreach (var index in CompleteTriangles(mesh.TriangleIndices, mesh.Vertices.Count))
                        result.Add(new OmsiMapPoint(mesh.Vertices[index].X, mesh.Vertices[index].Z));
                }
                else
                {
                    var mesh = OmsiXMesh.Parse(bytes);
                    foreach (var index in CompleteTriangles(mesh.TriangleIndices, mesh.Positions.Count))
                        result.Add(new OmsiMapPoint(mesh.Positions[index][0], mesh.Positions[index][2]));
                }
                result = ReplaceUprightPlanes(result);
                meshTriangles.Add(meshPath, result);
                return result;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException)
            {
                problem = exception.Message;
                meshProblems.Add(meshPath, problem);
                return null;
            }
        }

        /// <summary>Largest model that counts as a plant or sprite made of upright planes.</summary>
        private const double UprightPlaneMaxExtent = 20.0;

        /// <summary>
        /// Plants and sprites are often crossed upright planes that cover no area seen from above. A small model whose
        /// triangles together cover less than 5 % of its hull is replaced by the hull, which matches the crown. Fences
        /// keep their line, since their hull has no area either, and building walls are larger.
        /// </summary>
        private static List<OmsiMapPoint> ReplaceUprightPlanes(List<OmsiMapPoint> corners)
        {
            var hull = OmsiConvexHull.Compute(corners);
            if (hull.Length < 3)
                return corners;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var point in hull)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
            var hullArea = OmsiFootprintTracer.SignedArea(hull);
            if (hullArea < 0.5 || Math.Max(maxX - minX, maxY - minY) > UprightPlaneMaxExtent)
                return corners;

            var covered = 0.0;
            for (var index = 0; index + 2 < corners.Count; index += 3)
                covered += Math.Abs(OmsiFootprintTracer.SignedArea(new[] { corners[index], corners[index + 1], corners[index + 2] }));
            if (covered >= 0.05 * hullArea)
                return corners;

            var fan = new List<OmsiMapPoint>();
            for (var index = 1; index + 1 < hull.Length; index++)
                fan.AddRange(new[] { hull[0], hull[index], hull[index + 1] });
            return fan;
        }

        /// <summary>Vertex indices of the triangles whose three indices are valid; stray vertices are left out.</summary>
        private static IEnumerable<int> CompleteTriangles(List<int> triangleIndices, int vertexCount)
        {
            for (var first = 0; first + 2 < triangleIndices.Count; first += 3)
            {
                int a = triangleIndices[first], b = triangleIndices[first + 1], c = triangleIndices[first + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount)
                    continue;
                yield return a;
                yield return b;
                yield return c;
            }
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
            return new OmsiObjectOutline { Source = OmsiOutlineSource.Tree, Points = points, Contours = new[] { points } };
        }

        private static OmsiObjectOutline BoxOutline(double[] box, string problem)
        {
            double halfX = box[0] * 0.5, halfY = box[1] * 0.5, centerX = box[3], centerY = box[4];
            var points = new[]
            {
                new OmsiMapPoint(centerX - halfX, centerY - halfY), new OmsiMapPoint(centerX + halfX, centerY - halfY),
                new OmsiMapPoint(centerX + halfX, centerY + halfY), new OmsiMapPoint(centerX - halfX, centerY + halfY)
            };
            return new OmsiObjectOutline { Source = OmsiOutlineSource.BoundingBox, Problem = problem, Points = points, Contours = new[] { points } };
        }

        /// <summary>The area of all paths, each widened to its lane width.</summary>
        private static OmsiObjectOutline PathOutline(List<OmsiObjectPath> paths, string problem)
        {
            var corners = new List<OmsiMapPoint>();
            foreach (var path in paths)
            {
                var spline = new OmsiSplineRecord
                {
                    LocalX = path.X, LocalY = path.Y, HeadingDegrees = path.HeadingDegrees,
                    LengthMeters = path.LengthMeters, RadiusMeters = path.RadiusMeters
                };
                var centerLine = OmsiMapGeometry.BuildSplinePolyline(new OmsiTileSummary(), spline, 1e9);
                var half = Math.Max(path.WidthMeters, 0.1) * 0.5;
                OmsiMapPoint? lastLeft = null, lastRight = null;
                for (var index = 0; index < centerLine.Length; index++)
                {
                    var next = centerLine[Math.Min(index + 1, centerLine.Length - 1)];
                    var previous = centerLine[Math.Max(index - 1, 0)];
                    var dx = next.X - previous.X;
                    var dy = next.Y - previous.Y;
                    var length = Math.Sqrt(dx * dx + dy * dy);
                    if (length < 1e-9)
                        continue;
                    var left = new OmsiMapPoint(centerLine[index].X - dy / length * half, centerLine[index].Y + dx / length * half);
                    var right = new OmsiMapPoint(centerLine[index].X + dy / length * half, centerLine[index].Y - dx / length * half);
                    // The lane piece between two centre points as two triangles.
                    if (lastLeft.HasValue)
                        corners.AddRange(new[] { lastLeft.Value, lastRight.Value, right, lastLeft.Value, right, left });
                    lastLeft = left;
                    lastRight = right;
                }
            }
            var hull = OmsiConvexHull.Compute(corners);
            return new OmsiObjectOutline { Source = OmsiOutlineSource.Paths, Points = hull, Contours = TraceOrHull(corners, hull), Problem = problem };
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
