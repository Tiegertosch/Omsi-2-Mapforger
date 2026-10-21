using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A material of an object model with its texture resolved in the OMSI installation.</summary>
    public sealed class OmsiModelMaterial
    {
        /// <summary>Path relative to the OMSI installation, or null for an untextured material.</summary>
        public string TexturePath { get; internal set; }

        public float Red { get; internal set; } = 1f;
        public float Green { get; internal set; } = 1f;
        public float Blue { get; internal set; } = 1f;

        /// <summary>0 opaque; 1 the texture's alpha cuts; 2 it blends (see <see cref="OmsiSceneryMaterial"/>).</summary>
        public int AlphaMode { get; internal set; }
    }

    /// <summary>
    /// What an object shows, ready to draw: the visible meshes merged in the object frame (x right, y up, z forward),
    /// with triangles grouped by material. Trees have no meshes but a crown.
    /// </summary>
    public sealed class OmsiObjectModel
    {
        public List<float> Positions { get; } = new List<float>();
        public List<float> Normals { get; } = new List<float>();
        public List<float> Uvs { get; } = new List<float>();
        public List<OmsiModelMaterial> Materials { get; } = new List<OmsiModelMaterial>();

        /// <summary>Vertex indices per material, three per triangle.</summary>
        public List<List<int>> Triangles { get; } = new List<List<int>>();

        public int VertexCount => Positions.Count / 3;

        public bool IsTree { get; internal set; }

        /// <summary>
        /// True when the placement height is absolute rather than above the terrain: [absheight] or a crossing with
        /// [splinehelper].
        /// </summary>
        public bool HasAbsoluteHeight { get; internal set; }

        /// <summary>Crown radius of a tree in metres.</summary>
        public double TreeRadius { get; internal set; }

        /// <summary>Mean height of a tree in metres; OMSI picks each tree's height in its range at random.</summary>
        public double TreeHeight { get; internal set; }

        /// <summary>The tree texture relative to the OMSI installation, or null.</summary>
        public string TreeTexturePath { get; internal set; }
    }

    /// <summary>
    /// Builds and caches drawable models for the scenery objects of one OMSI installation. Shown are the meshes that
    /// are not editor helpers, belong to no [LOD] level or the first, most detailed one, and whose [visible] condition
    /// holds with the default variables: all 0, except that it is summer.
    /// </summary>
    public sealed class OmsiObjectModelCatalog
    {
        private static readonly Dictionary<string, double> DefaultVariables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "sommerhalbjahr", 1.0 }
        };

        private readonly string installationRoot;
        private readonly Dictionary<string, OmsiObjectModel> models = new Dictionary<string, OmsiObjectModel>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> resolvedTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public OmsiObjectModelCatalog(string installationRoot)
        {
            this.installationRoot = Path.GetFullPath(installationRoot);
        }

        /// <summary>Problems met while reading, such as missing meshes or textures.</summary>
        public List<string> Problems { get; } = new List<string>();

        /// <summary>The model, or null when the object file is missing or unreadable.</summary>
        public OmsiObjectModel Get(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
            if (models.TryGetValue(assetPath, out var cached))
                return cached;
            var model = Build(assetPath);
            models.Add(assetPath, model);
            return model;
        }

        private OmsiObjectModel Build(string assetPath)
        {
            OmsiSceneryObject sceneryObject;
            string objectFolder;
            try
            {
                var scoPath = OmsiRelativePath.Resolve(installationRoot, assetPath);
                if (!File.Exists(scoPath))
                {
                    Problems.Add(assetPath + " not found");
                    return null;
                }
                sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(scoPath));
                objectFolder = Path.GetDirectoryName(assetPath) ?? string.Empty;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Problems.Add(assetPath + ": " + exception.Message);
                return null;
            }

            var model = new OmsiObjectModel { HasAbsoluteHeight = sceneryObject.HasAbsoluteHeight || sceneryObject.HasSplineHelper };
            if (sceneryObject.Tree != null)
            {
                var tree = sceneryObject.Tree;
                model.IsTree = true;
                model.TreeHeight = 0.5 * (tree.MinHeight + tree.MaxHeight);
                model.TreeRadius = 0.25 * (tree.MinHeight + tree.MaxHeight) * 0.5 * (tree.MinWidthRatio + tree.MaxWidthRatio);
                model.TreeTexturePath = ResolveTexture(objectFolder, tree.Texture);
                return model;
            }

            foreach (var mesh in sceneryObject.Meshes)
            {
                if (mesh.EditorOnly || mesh.LodLevel > 0 || !IsVisible(mesh))
                    continue;
                AddMesh(model, objectFolder, mesh, assetPath);
            }
            return model;
        }

        private static bool IsVisible(OmsiSceneryMesh mesh)
        {
            if (mesh.VisibleVariable == null)
                return true;
            DefaultVariables.TryGetValue(mesh.VisibleVariable, out var value);
            return Math.Abs(value - mesh.VisibleValue) < 0.5;
        }

        private void AddMesh(OmsiObjectModel model, string objectFolder, OmsiSceneryMesh mesh, string assetPath)
        {
            var meshPath = Path.Combine(objectFolder, "model", mesh.FileName);
            byte[] bytes;
            try
            {
                var fullPath = OmsiRelativePath.Resolve(installationRoot, meshPath);
                if (!File.Exists(fullPath))
                {
                    Problems.Add(meshPath + " not found (" + assetPath + ")");
                    return;
                }
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Problems.Add(meshPath + ": " + exception.Message);
                return;
            }

            try
            {
                if (bytes.Length >= 2 && bytes[0] == 0x84 && bytes[1] == 0x19)
                    AddO3d(model, objectFolder, mesh, OmsiO3dMesh.Parse(bytes));
                else
                    AddX(model, OmsiXMesh.Parse(bytes));
            }
            catch (InvalidDataException exception)
            {
                Problems.Add(meshPath + ": " + exception.Message);
            }
        }

        private void AddO3d(OmsiObjectModel model, string objectFolder, OmsiSceneryMesh sceneryMesh, OmsiO3dMesh mesh)
        {
            var first = model.VertexCount;
            foreach (var vertex in mesh.Vertices)
            {
                model.Positions.AddRange(new[] { vertex.X, vertex.Y, vertex.Z });
                model.Normals.AddRange(new[] { vertex.NormalX, vertex.NormalY, vertex.NormalZ });
                model.Uvs.AddRange(new[] { vertex.U, vertex.V });
            }

            // Materials of this mesh, then their triangles; [matl] picks a material by texture name and occurrence.
            var materialSlots = new int[Math.Max(1, mesh.Materials.Count)];
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < mesh.Materials.Count; index++)
            {
                var source = mesh.Materials[index];
                var texture = source.Texture ?? string.Empty;
                occurrences.TryGetValue(texture, out var occurrence);
                occurrences[texture] = occurrence + 1;
                var settings = sceneryMesh.Materials.Find(item => string.Equals(item.Texture, texture, StringComparison.OrdinalIgnoreCase) && item.Index == occurrence);
                materialSlots[index] = AddMaterial(model, new OmsiModelMaterial
                {
                    TexturePath = texture.Length > 0 ? ResolveTexture(objectFolder, texture) : null,
                    Red = source.DiffuseRed,
                    Green = source.DiffuseGreen,
                    Blue = source.DiffuseBlue,
                    AlphaMode = settings?.AlphaMode ?? 0
                });
            }
            if (mesh.Materials.Count == 0)
                materialSlots[0] = AddMaterial(model, new OmsiModelMaterial { Red = 0.7f, Green = 0.7f, Blue = 0.7f });

            for (var triangle = 0; triangle * 3 + 2 < mesh.TriangleIndices.Count; triangle++)
            {
                var material = triangle < mesh.TriangleMaterials.Count ? mesh.TriangleMaterials[triangle] : 0;
                var slot = materialSlots[material >= 0 && material < materialSlots.Length ? material : 0];
                for (var corner = 0; corner < 3; corner++)
                    model.Triangles[slot].Add(first + mesh.TriangleIndices[triangle * 3 + corner]);
            }
        }

        /// <summary>Text .x meshes are drawn untextured in grey with flat normals; they are rare in OMSI scenery.</summary>
        private static void AddX(OmsiObjectModel model, OmsiXMesh mesh)
        {
            var slot = AddMaterial(model, new OmsiModelMaterial { Red = 0.7f, Green = 0.7f, Blue = 0.7f });
            for (var index = 0; index + 2 < mesh.TriangleIndices.Count; index += 3)
            {
                var a = mesh.Positions[mesh.TriangleIndices[index]];
                var b = mesh.Positions[mesh.TriangleIndices[index + 1]];
                var c = mesh.Positions[mesh.TriangleIndices[index + 2]];
                // Face normal from the left-handed winding Direct3D uses.
                double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                double vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length < 1e-12)
                    continue;
                foreach (var corner in new[] { a, b, c })
                {
                    model.Triangles[slot].Add(model.VertexCount);
                    model.Positions.AddRange(corner);
                    model.Normals.AddRange(new[] { (float)(-nx / length), (float)(-ny / length), (float)(-nz / length) });
                    model.Uvs.AddRange(new[] { 0f, 0f });
                }
            }
        }

        /// <summary>Reuses an equal material, so objects made of several meshes keep few draw calls.</summary>
        private static int AddMaterial(OmsiObjectModel model, OmsiModelMaterial material)
        {
            for (var index = 0; index < model.Materials.Count; index++)
            {
                var existing = model.Materials[index];
                if (string.Equals(existing.TexturePath, material.TexturePath, StringComparison.OrdinalIgnoreCase) &&
                    existing.AlphaMode == material.AlphaMode && existing.Red == material.Red &&
                    existing.Green == material.Green && existing.Blue == material.Blue)
                    return index;
            }
            model.Materials.Add(material);
            model.Triangles.Add(new List<int>());
            return model.Materials.Count - 1;
        }

        /// <summary>
        /// The texture as OMSI finds it: a .dds of the same name first, then the named file, in the object's texture
        /// folder and then in the global Texture folder. Null when none exists.
        /// </summary>
        private string ResolveTexture(string objectFolder, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            var key = objectFolder + "|" + name;
            if (resolvedTextures.TryGetValue(key, out var cached))
                return cached;

            string result = null;
            var dds = Path.ChangeExtension(name.Trim(), ".dds");
            foreach (var candidate in new[]
                     {
                         Path.Combine(objectFolder, "texture", dds), Path.Combine(objectFolder, "texture", name.Trim()),
                         Path.Combine("Texture", dds), Path.Combine("Texture", name.Trim())
                     })
            {
                try
                {
                    if (File.Exists(OmsiRelativePath.Resolve(installationRoot, candidate)))
                    {
                        result = candidate;
                        break;
                    }
                }
                catch (ArgumentException)
                {
                    // A name that leaves the installation is never found.
                }
            }
            if (result == null)
                Problems.Add("Texture " + Path.Combine(objectFolder, "texture", name.Trim()) + " not found");
            resolvedTextures[key] = result;
            return result;
        }
    }
}
