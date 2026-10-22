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

        /// <summary>Opacity of the diffuse colour; below 1 the material is see-through, like plain glass.</summary>
        public float Alpha { get; internal set; } = 1f;

        /// <summary>0 opaque; 1 the texture's alpha cuts; 2 it blends (see <see cref="OmsiSceneryMaterial"/>).</summary>
        public int AlphaMode { get; internal set; }

        /// <summary>The texture whose alpha gives the opacity ([matl_transmap]), relative to the installation, or null.</summary>
        public string TransparencyTexturePath { get; internal set; }

        /// <summary>
        /// The texture is made at run time by a script, such as a display text; <see cref="TexturePath"/> is then null,
        /// as the file is only a placeholder.
        /// </summary>
        public bool HasGeneratedTexture { get; internal set; }

        /// <summary>True when the material is drawn blended over what lies behind it.</summary>
        public bool IsTransparent => AlphaMode == 2 || Alpha < 0.99f;
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

        /// <summary>
        /// The object joins splines through [splinehelper], like a crossing. Tilting it would tear the joined roads
        /// apart, so the editor keeps its pitch and bank.
        /// </summary>
        public bool HasSplineHelper { get; internal set; }

        /// <summary>Points where attached objects hang, from [new_attachment].</summary>
        public IReadOnlyList<OmsiAttachPoint> AttachPoints { get; internal set; } = new OmsiAttachPoint[0];

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

            var model = new OmsiObjectModel
            {
                HasAbsoluteHeight = sceneryObject.HasAbsoluteHeight || sceneryObject.HasSplineHelper,
                HasSplineHelper = sceneryObject.HasSplineHelper,
                AttachPoints = sceneryObject.AttachPoints
            };
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
                    AddX(model, objectFolder, mesh, OmsiXMesh.Parse(bytes));
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

            // Materials of this mesh, then their triangles; a mesh without materials is grey.
            var materialSlots = mesh.Materials.Count > 0
                ? MaterialSlots(model, objectFolder, sceneryMesh, mesh.Materials)
                : new[] { AddMaterial(model, new OmsiModelMaterial { Red = 0.7f, Green = 0.7f, Blue = 0.7f }) };

            for (var triangle = 0; triangle * 3 + 2 < mesh.TriangleIndices.Count; triangle++)
            {
                var material = triangle < mesh.TriangleMaterials.Count ? mesh.TriangleMaterials[triangle] : 0;
                var slot = materialSlots[material >= 0 && material < materialSlots.Length ? material : 0];
                for (var corner = 0; corner < 3; corner++)
                    model.Triangles[slot].Add(first + mesh.TriangleIndices[triangle * 3 + corner]);
            }
        }

        /// <summary>
        /// Text .x meshes with their materials, normals and texture coordinates. Where the file gives no normal, the
        /// vertex takes the normal of its first face.
        /// </summary>
        private void AddX(OmsiObjectModel model, string objectFolder, OmsiSceneryMesh sceneryMesh, OmsiXMesh mesh)
        {
            var first = model.VertexCount;
            var normals = new float[mesh.Positions.Count][];
            for (var index = 0; index < normals.Length; index++)
                normals[index] = mesh.Normals[index];
            for (var index = 0; index + 2 < mesh.TriangleIndices.Count; index += 3)
            {
                var a = mesh.Positions[mesh.TriangleIndices[index]];
                var b = mesh.Positions[mesh.TriangleIndices[index + 1]];
                var c = mesh.Positions[mesh.TriangleIndices[index + 2]];
                double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                double vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length < 1e-12)
                    continue;
                // Direct3D's clockwise front faces in a left-handed frame: the cross product points to the back.
                var face = new[] { (float)(-nx / length), (float)(-ny / length), (float)(-nz / length) };
                for (var corner = 0; corner < 3; corner++)
                    normals[mesh.TriangleIndices[index + corner]] = normals[mesh.TriangleIndices[index + corner]] ?? face;
            }
            for (var index = 0; index < mesh.Positions.Count; index++)
            {
                model.Positions.AddRange(mesh.Positions[index]);
                model.Normals.AddRange(normals[index] ?? new[] { 0f, 1f, 0f });
                model.Uvs.AddRange(mesh.Uvs[index]);
            }

            var slots = MaterialSlots(model, objectFolder, sceneryMesh, mesh.Materials);
            var fallback = -1;
            for (var triangle = 0; triangle * 3 + 2 < mesh.TriangleIndices.Count; triangle++)
            {
                var material = mesh.TriangleMaterials[triangle];
                int slot;
                if (material >= 0 && material < slots.Length)
                    slot = slots[material];
                else
                    slot = fallback >= 0 ? fallback : fallback = AddMaterial(model, new OmsiModelMaterial { Red = 0.7f, Green = 0.7f, Blue = 0.7f });
                for (var corner = 0; corner < 3; corner++)
                    model.Triangles[slot].Add(first + mesh.TriangleIndices[triangle * 3 + corner]);
            }
        }

        /// <summary>
        /// The model's material for each material of a mesh. [matl] picks a mesh material by its texture name and its
        /// occurrence among the materials with that name and sets its alpha mode.
        /// </summary>
        private int[] MaterialSlots(OmsiObjectModel model, string objectFolder, OmsiSceneryMesh sceneryMesh, List<OmsiMeshMaterial> materials)
        {
            var slots = new int[materials.Count];
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < materials.Count; index++)
            {
                var source = materials[index];
                var texture = source.Texture ?? string.Empty;
                occurrences.TryGetValue(texture, out var occurrence);
                occurrences[texture] = occurrence + 1;
                var settings = sceneryMesh.Materials.Find(item => string.Equals(item.Texture, texture, StringComparison.OrdinalIgnoreCase) && item.Index == occurrence);
                var generated = settings?.HasGeneratedTexture ?? false;
                slots[index] = AddMaterial(model, new OmsiModelMaterial
                {
                    TexturePath = texture.Length > 0 && !generated ? ResolveTexture(objectFolder, texture) : null,
                    HasGeneratedTexture = generated,
                    Red = source.DiffuseRed,
                    Green = source.DiffuseGreen,
                    Blue = source.DiffuseBlue,
                    Alpha = source.DiffuseAlpha,
                    AlphaMode = settings?.AlphaMode ?? 0,
                    TransparencyTexturePath = settings?.TransparencyTexture != null ? ResolveTexture(objectFolder, settings.TransparencyTexture) : null
                });
            }
            return slots;
        }

        /// <summary>Reuses an equal material, so objects made of several meshes keep few draw calls.</summary>
        private static int AddMaterial(OmsiObjectModel model, OmsiModelMaterial material)
        {
            for (var index = 0; index < model.Materials.Count; index++)
            {
                var existing = model.Materials[index];
                if (string.Equals(existing.TexturePath, material.TexturePath, StringComparison.OrdinalIgnoreCase) &&
                    existing.AlphaMode == material.AlphaMode && existing.Red == material.Red &&
                    existing.HasGeneratedTexture == material.HasGeneratedTexture &&
                    string.Equals(existing.TransparencyTexturePath, material.TransparencyTexturePath, StringComparison.OrdinalIgnoreCase) &&
                    existing.Green == material.Green && existing.Blue == material.Blue && existing.Alpha == material.Alpha)
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
