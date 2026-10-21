using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.Rendering;

namespace OmsiMapEditor.World
{
    /// <summary>
    /// Draws the scenery objects of a map with their models and textures, seen from above by the ground camera. Every
    /// object kind becomes one mesh with a material per texture; its placements are drawn with GPU instancing. Objects
    /// stand on the terrain unless their height is absolute. Trees are drawn as a disc of their crown.
    /// </summary>
    public sealed class MapObjectRenderer : MonoBehaviour
    {
        /// <summary>Textures are reduced to this edge length; the view from above never needs more.</summary>
        private const int MaximumTextureSize = 1024;

        private const float UploadBudgetMilliseconds = 8f;

        /// <summary>Objects come after the spline surfaces, which they stand on.</summary>
        private const int ObjectQueue = 3150;

        private const int InstancesPerCall = 1023;

        private sealed class TextureData
        {
            public OmsiCompressedImage Compressed;
            public OmsiImage Image;
            public byte[] Encoded;
        }

        private sealed class KindData
        {
            public string AssetPath;
            public OmsiObjectModel Model;

            /// <summary>Average colour of a tree's opaque texels; white for other objects.</summary>
            public Color TreeColour = Color.white;

            /// <summary>The crown in a tree's side-view texture as offset and size in texture coordinates, or null.</summary>
            public Vector4? CrownArea;

            public string TreeTexturePath;

            public Mesh Mesh;
            public Material[] Materials;
            public Matrix4x4[] Instances = new Matrix4x4[0];
            public int InstanceCount;
        }

        private sealed class LoadData
        {
            public readonly Dictionary<long, OmsiTerrain> Terrains = new Dictionary<long, OmsiTerrain>();
            public readonly List<KindData> Kinds = new List<KindData>();
            public readonly Dictionary<string, TextureData> Textures = new Dictionary<string, TextureData>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Problems = new List<string>();
        }

        // An instancing material in Resources keeps the instancing variant of the shader in a build; new materials copy it.
        private Material template;
        private Mesh treeDisc;
        private Mesh treeCrown;
        private Camera targetCamera;
        private OmsiMapSummary summary;
        private string installationRoot;
        private double tileSize;
        private double originX;
        private double originY;
        private int generation;
        private int loadingGeneration;
        private Task<LoadData> loading;
        private LoadData pending;
        private int pendingKind;
        private bool placementsDirty;

        // Kinds requested once; a kind whose files are missing is not read again and again.
        private readonly HashSet<string> requestedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only the read of a whole map reports; reading a newly placed kind stays quiet.
        private bool reportWhenUploaded;
        private bool objectsVisible = true;
        private readonly Dictionary<long, OmsiTerrain> terrains = new Dictionary<long, OmsiTerrain>();

        /// <summary>Where each placement's matrix lives, so a drag can move single instances.</summary>
        private readonly Dictionary<OmsiObjectRecord, KeyValuePair<KindData, int>> instanceOf =
            new Dictionary<OmsiObjectRecord, KeyValuePair<KindData, int>>();
        private readonly Dictionary<string, KindData> kinds = new Dictionary<string, KindData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        /// <summary>Raised on the main thread when the objects of a map are ready, with a short report.</summary>
        public event Action<string> Loaded;

        /// <summary>True when objects of this file are drawn with a model or, for trees, a crown.</summary>
        public bool HasModel(string assetPath)
        {
            return assetPath != null && kinds.TryGetValue(assetPath, out var kind) && kind.Mesh != null;
        }

        /// <summary>Raised when the set of kinds with a model changed, such as after reading a newly placed kind.</summary>
        public event Action ModelsChanged;

        /// <summary>True while models are read or uploaded or placements wait to be updated.</summary>
        public bool IsLoading => loading != null || pending != null || placementsDirty;

        public bool ObjectsVisible
        {
            get => objectsVisible;
            set => objectsVisible = value;
        }

        private void Awake()
        {
            template = Resources.Load<Material>("MapObjectSurface");
            if (template == null)
                Debug.LogError("MapObjectSurface.mat was not found in Resources.");
            treeDisc = CreateDisc();
            treeCrown = CreateCrown();
        }

        /// <summary>The camera that shows the objects; the ground camera.</summary>
        public void SetCamera(Camera camera)
        {
            targetCamera = camera;
        }

        /// <summary>
        /// Reads terrain heights, object models and their textures in the background; the objects appear once their
        /// kinds are uploaded. Terrain comes through <paramref name="source"/>, models and textures from the installation.
        /// </summary>
        public void Load(OmsiMapSummary map, IOmsiFileSource source, string installationRootPath, double mapOriginX, double mapOriginY)
        {
            Clear();
            if (map == null || source == null)
                return;

            summary = map;
            installationRoot = installationRootPath;
            tileSize = map.TileSizeMeters;
            originX = mapOriginX;
            originY = mapOriginY;
            var tiles = new List<KeyValuePair<long, string>>();
            var assetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in map.Tiles)
            {
                if (!tile.IsAvailable)
                    continue;
                tiles.Add(new KeyValuePair<long, string>(TileKey(tile.X, tile.Y), tile.FileName));
                foreach (var item in tile.Objects)
                    assetPaths.Add(item.AssetPath);
            }
            requestedKinds.UnionWith(assetPaths);
            reportWhenUploaded = true;
            loadingGeneration = generation;
            loading = Task.Run(() => Read(tiles, new List<string>(assetPaths), source, installationRootPath));
        }

        /// <summary>
        /// Takes object edits into account: moved, added and removed objects. Kinds not loaded yet, such as a newly
        /// placed object, are read in the background.
        /// </summary>
        public void RefreshPlacements()
        {
            placementsDirty = true;
        }

        public void Clear()
        {
            generation++;
            loading = null;
            pending = null;
            pendingKind = 0;
            summary = null;
            requestedKinds.Clear();
            terrains.Clear();
            instanceOf.Clear();
            kinds.Clear();
            textures.Clear();
            foreach (var item in owned)
                Destroy(item);
            owned.Clear();
        }

        private static long TileKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        private void Update()
        {
            if (loading != null && loading.IsCompleted)
            {
                var task = loading;
                loading = null;
                if (loadingGeneration != generation)
                    return;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    Loaded?.Invoke("Objects could not be read: " + task.Exception?.GetBaseException().Message);
                    return;
                }
                pending = task.Result;
                pendingKind = 0;
                foreach (var pair in pending.Terrains)
                    terrains[pair.Key] = pair.Value;
            }

            if (pending != null)
                ContinueUpload();

            if (placementsDirty && pending == null && loading == null && summary != null)
            {
                placementsDirty = false;
                UpdatePlacements();
            }
        }

        private void LateUpdate()
        {
            if (!objectsVisible || targetCamera == null || !targetCamera.enabled)
                return;
            foreach (var kind in kinds.Values)
            {
                if (kind.Mesh == null || kind.InstanceCount == 0)
                    continue;
                for (var submesh = 0; submesh < kind.Materials.Length; submesh++)
                {
                    var material = kind.Materials[submesh];
                    if (material == null)
                        continue;
                    var parameters = new RenderParams(material)
                    {
                        camera = targetCamera,
                        layer = MapGroundRenderer.GroundLayer,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                        // The instances spread over the whole map; a box around all of it keeps them from being culled.
                        worldBounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e5f, 1e6f))
                    };
                    for (var start = 0; start < kind.InstanceCount; start += InstancesPerCall)
                        Graphics.RenderMeshInstanced(parameters, kind.Mesh, submesh, kind.Instances,
                            Math.Min(InstancesPerCall, kind.InstanceCount - start), start);
                }
            }
        }

        private static LoadData Read(List<KeyValuePair<long, string>> tiles, List<string> assetPaths, IOmsiFileSource source,
            string installationRoot)
        {
            var data = new LoadData();
            foreach (var tile in tiles)
            {
                var path = OmsiRelativePath.Normalize(tile.Value) + ".terrain";
                try
                {
                    if (source.FileExists(path))
                        data.Terrains[tile.Key] = OmsiTerrain.Parse(source.ReadAllBytes(path));
                }
                catch (InvalidDataException exception)
                {
                    data.Problems.Add(path + ": " + exception.Message);
                }
            }
            ReadKinds(assetPaths, installationRoot, data);
            return data;
        }

        private static void ReadKinds(List<string> assetPaths, string installationRoot, LoadData data)
        {
            var catalog = new OmsiObjectModelCatalog(installationRoot);
            foreach (var assetPath in assetPaths)
            {
                var model = catalog.Get(assetPath);
                if (model == null)
                    continue;
                var kind = new KindData { AssetPath = assetPath, Model = model };
                if (model.IsTree)
                {
                    var tree = ReadTexture(installationRoot, model.TreeTexturePath, data);
                    kind.TreeColour = AverageOpaqueColour(tree?.Image);
                    kind.CrownArea = CrownArea(tree?.Image);
                    kind.TreeTexturePath = model.TreeTexturePath;
                }
                else
                {
                    foreach (var material in model.Materials)
                        ReadTexture(installationRoot, material.TexturePath, data);
                }
                data.Kinds.Add(kind);
            }
            data.Problems.AddRange(catalog.Problems);
        }

        private static TextureData ReadTexture(string installationRoot, string relativePath, LoadData data)
        {
            if (relativePath == null)
                return null;
            if (data.Textures.TryGetValue(relativePath, out var existing))
                return existing;

            var texture = new TextureData();
            try
            {
                var bytes = File.ReadAllBytes(OmsiRelativePath.Resolve(installationRoot, relativePath));
                // DXT1 and DXT5 go to the graphics card as they are; DXT3 has no engine format and is decoded.
                if (OmsiImageReader.TryReadCompressed(bytes, MaximumTextureSize, out var compressed) && compressed.Format != OmsiBlockFormat.Dxt3 &&
                    compressed.Width % 4 == 0 && compressed.Height % 4 == 0)
                    texture.Compressed = compressed;
                else if (OmsiImageReader.TryDecode(bytes, out var image))
                    texture.Image = image.Reduce(MaximumTextureSize);
                else
                    texture.Encoded = bytes;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                data.Problems.Add(relativePath + ": " + exception.Message);
            }
            data.Textures[relativePath] = texture;
            return texture;
        }

        /// <summary>
        /// The crown of a side-view tree texture: the upper 70 % of the opaque texels, below them the trunk. Returned as
        /// the rectangle around the crown in texture coordinates with v from the top, so the round crown mesh shows the
        /// crown and never reaches past the image into the trunk; null without an alpha channel.
        /// </summary>
        private static Vector4? CrownArea(OmsiImage image)
        {
            if (image == null || image.Channels != 4)
                return null;
            int top = -1, bottom = -1;
            for (var row = 0; row < image.Height; row++)
            {
                for (var column = 0; column < image.Width; column++)
                {
                    if (image.Pixels[(row * image.Width + column) * 4 + 3] < 128)
                        continue;
                    if (top < 0)
                        top = row;
                    bottom = row;
                    break;
                }
            }
            if (top < 0)
                return null;

            var crownBottom = top + (int)((bottom - top) * 0.7);
            int left = image.Width, right = -1;
            for (var row = top; row <= crownBottom; row++)
                for (var column = 0; column < image.Width; column++)
                    if (image.Pixels[(row * image.Width + column) * 4 + 3] >= 128)
                    {
                        left = Math.Min(left, column);
                        right = Math.Max(right, column);
                    }
            if (right < left)
                return null;

            return new Vector4((float)left / image.Width, (float)top / image.Height,
                (float)(right - left + 1) / image.Width, (float)(crownBottom - top + 1) / image.Height);
        }

        /// <summary>The mean colour of the texels that are not transparent, darkened a little like a crown in shade.</summary>
        private static Color AverageOpaqueColour(OmsiImage image)
        {
            if (image == null || image.Channels != 4)
                return new Color(0.3f, 0.45f, 0.25f, 1f);
            double red = 0, green = 0, blue = 0, count = 0;
            for (var index = 0; index < image.Pixels.Length; index += 4)
            {
                if (image.Pixels[index + 3] < 128)
                    continue;
                red += image.Pixels[index];
                green += image.Pixels[index + 1];
                blue += image.Pixels[index + 2];
                count++;
            }
            if (count == 0)
                return new Color(0.3f, 0.45f, 0.25f, 1f);
            return new Color((float)(red / count / 255 * 0.85), (float)(green / count / 255 * 0.85), (float)(blue / count / 255 * 0.85), 1f);
        }

        private void ContinueUpload()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (pendingKind < pending.Kinds.Count && watch.Elapsed.TotalMilliseconds < UploadBudgetMilliseconds)
            {
                var kind = pending.Kinds[pendingKind++];
                BuildKind(kind);
                kinds[kind.AssetPath] = kind;
            }
            if (pendingKind < pending.Kinds.Count)
                return;

            var data = pending;
            pending = null;
            placementsDirty = true;
            ModelsChanged?.Invoke();
            if (data.Problems.Count > 0)
                Debug.LogWarning("Object models: " + data.Problems.Count + " problem(s)\n" + string.Join("\n", data.Problems));
            if (!reportWhenUploaded)
                return;
            reportWhenUploaded = false;
            Loaded?.Invoke("Object models ready: " + data.Kinds.Count + " kinds, " + textures.Count + " textures" +
                           (data.Problems.Count > 0 ? "; " + data.Problems.Count + " problem(s), first: " + data.Problems[0] : string.Empty) + ".");
        }

        private void BuildKind(KindData kind)
        {
            if (template == null)
                return;
            var model = kind.Model;
            if (model.IsTree)
            {
                if (kind.CrownArea.HasValue)
                {
                    // The crown of the texture on a dome, cut out where the leaves are transparent.
                    kind.Mesh = treeCrown;
                    var crown = NewMaterial(TextureFor(kind.TreeTexturePath), Color.white, 0.5f);
                    crown.SetVector("_UvRect", kind.CrownArea.Value);
                    kind.Materials = new[] { crown };
                }
                else
                {
                    kind.Mesh = treeDisc;
                    kind.Materials = new[] { NewMaterial(Texture2D.whiteTexture, kind.TreeColour, 0f) };
                }
                return;
            }
            if (model.VertexCount == 0)
                return;

            var mesh = new Mesh { name = kind.AssetPath };
            if (model.VertexCount > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            var vertices = new Vector3[model.VertexCount];
            var normals = new Vector3[model.VertexCount];
            var uvs = new Vector2[model.VertexCount];
            for (var index = 0; index < vertices.Length; index++)
            {
                vertices[index] = new Vector3(model.Positions[index * 3], model.Positions[index * 3 + 1], model.Positions[index * 3 + 2]);
                normals[index] = new Vector3(model.Normals[index * 3], model.Normals[index * 3 + 1], model.Normals[index * 3 + 2]);
                uvs[index] = new Vector2(model.Uvs[index * 2], model.Uvs[index * 2 + 1]);
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.subMeshCount = model.Materials.Count;
            kind.Materials = new Material[model.Materials.Count];
            for (var index = 0; index < model.Materials.Count; index++)
            {
                mesh.SetTriangles(model.Triangles[index], index, false);
                if (model.Triangles[index].Count == 0)
                    continue;
                var source = model.Materials[index];
                kind.Materials[index] = NewMaterial(TextureFor(source.TexturePath), new Color(source.Red, source.Green, source.Blue, 1f),
                    source.AlphaMode > 0 ? 0.5f : 0f);
            }
            mesh.RecalculateBounds();
            owned.Add(mesh);
            kind.Mesh = mesh;
        }

        private Material NewMaterial(Texture texture, Color colour, float cutoff)
        {
            var material = new Material(template) { enableInstancing = true, renderQueue = ObjectQueue };
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", colour);
            material.SetFloat("_Cutoff", cutoff);
            owned.Add(material);
            return material;
        }

        /// <summary>
        /// Uploads a texture once. Direct3D counts v from the top of the image and Unity stores the bottom row first, so
        /// rows are uploaded in file order and the meshes keep their texture coordinates.
        /// </summary>
        private Texture TextureFor(string relativePath)
        {
            if (relativePath == null)
                return Texture2D.whiteTexture;
            if (textures.TryGetValue(relativePath, out var existing))
                return existing;
            if (pending == null || !pending.Textures.TryGetValue(relativePath, out var data))
                return Texture2D.whiteTexture;

            Texture2D texture = null;
            if (data.Compressed != null)
            {
                var compressed = data.Compressed;
                texture = new Texture2D(compressed.Width, compressed.Height,
                    compressed.Format == OmsiBlockFormat.Dxt1 ? TextureFormat.DXT1 : TextureFormat.DXT5, compressed.Levels.Count, false);
                for (var level = 0; level < compressed.Levels.Count; level++)
                    texture.SetPixelData(compressed.Levels[level], level);
                texture.Apply(false, true);
            }
            else if (data.Image != null)
            {
                texture = UploadImage(data.Image);
            }
            else if (data.Encoded != null)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (texture.LoadImage(data.Encoded, false))
                {
                    // LoadImage stores the image upright for Unity; turn it over to match the Direct3D coordinates.
                    var pixels = texture.GetPixels32();
                    var flipped = new Color32[pixels.Length];
                    for (var row = 0; row < texture.height; row++)
                        Array.Copy(pixels, row * texture.width, flipped, (texture.height - 1 - row) * texture.width, texture.width);
                    texture.SetPixels32(flipped);
                    texture.Apply(true, false);
                    texture.Compress(false);
                    texture.Apply(false, true);
                }
                else
                {
                    Destroy(texture);
                    texture = null;
                }
            }
            if (texture == null)
                return Texture2D.whiteTexture;

            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            owned.Add(texture);
            textures[relativePath] = texture;
            return texture;
        }

        /// <summary>RGBA without transparency becomes DXT1, with transparency DXT5; both a quarter or less of the size.</summary>
        private static Texture2D UploadImage(OmsiImage image)
        {
            var opaque = true;
            var rgba = image.Pixels;
            if (image.Channels == 1)
            {
                rgba = new byte[image.Width * image.Height * 4];
                for (var index = 0; index < image.Pixels.Length; index++)
                {
                    rgba[index * 4] = rgba[index * 4 + 1] = rgba[index * 4 + 2] = image.Pixels[index];
                    rgba[index * 4 + 3] = 255;
                }
            }
            else
            {
                for (var index = 3; index < rgba.Length && opaque; index += 4)
                    opaque = rgba[index] == 255;
            }

            Texture2D texture;
            if (opaque)
            {
                var rgb = new byte[image.Width * image.Height * 3];
                for (int source = 0, target = 0; target < rgb.Length; source += 4, target += 3)
                {
                    rgb[target] = rgba[source];
                    rgb[target + 1] = rgba[source + 1];
                    rgb[target + 2] = rgba[source + 2];
                }
                texture = new Texture2D(image.Width, image.Height, TextureFormat.RGB24, true);
                texture.SetPixelData(rgb, 0);
            }
            else
            {
                texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, true);
                texture.SetPixelData(rgba, 0);
            }
            texture.Apply(true, false);
            if (image.Width % 4 == 0 && image.Height % 4 == 0)
                texture.Compress(false);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Instance matrices for every placement: position on the map, standing on the terrain unless absolute, turned
        /// clockwise by the heading as OMSI does. Unknown kinds, such as newly placed ones, are read first.
        /// </summary>
        private void UpdatePlacements()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            foreach (var tile in summary.Tiles)
                foreach (var item in tile.Objects)
                {
                    counts.TryGetValue(item.AssetPath, out var count);
                    counts[item.AssetPath] = count + 1;
                    if (count == 0 && !kinds.ContainsKey(item.AssetPath) && !requestedKinds.Contains(item.AssetPath))
                        missing.Add(item.AssetPath);
                }
            if (missing.Count > 0 && loading == null)
            {
                requestedKinds.UnionWith(missing);
                loadingGeneration = generation;
                var root = installationRoot;
                loading = Task.Run(() =>
                {
                    var data = new LoadData();
                    ReadKinds(missing, root, data);
                    return data;
                });
                placementsDirty = true;
            }

            foreach (var kind in kinds.Values)
            {
                counts.TryGetValue(kind.AssetPath, out var count);
                if (kind.Instances.Length < count)
                    kind.Instances = new Matrix4x4[count];
                kind.InstanceCount = 0;
            }
            instanceOf.Clear();
            foreach (var tile in summary.Tiles)
            {
                foreach (var item in tile.Objects)
                {
                    if (!kinds.TryGetValue(item.AssetPath, out var kind) || kind.Mesh == null)
                        continue;
                    instanceOf[item] = new KeyValuePair<KindData, int>(kind, kind.InstanceCount);
                    kind.Instances[kind.InstanceCount++] = PlacementMatrix(kind.Model, tile, item.LocalX, item.LocalY, item.Height, item.HeadingDegrees);
                }
            }
        }

        /// <summary>
        /// Draws dragged objects at their preview position until the drag ends; the offset is in metres and the same
        /// for every object, which may lie in different tiles.
        /// </summary>
        public void PreviewMove(IEnumerable<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>> objects, double offsetX, double offsetY)
        {
            foreach (var pair in objects)
                PreviewPlacement(pair.Key, pair.Value, pair.Value.LocalX + offsetX, pair.Value.LocalY + offsetY, pair.Value.HeadingDegrees);
        }

        /// <summary>Draws an object turned to a preview heading until the drag ends.</summary>
        public void PreviewHeading(OmsiTileSummary tile, OmsiObjectRecord item, double headingDegrees)
        {
            if (item != null)
                PreviewPlacement(tile, item, item.LocalX, item.LocalY, headingDegrees);
        }

        /// <summary>Returns to the stored placements; a committed edit arrives through <see cref="RefreshPlacements"/>.</summary>
        public void EndPreview()
        {
            placementsDirty = true;
        }

        private void PreviewPlacement(OmsiTileSummary tile, OmsiObjectRecord item, double localX, double localY, double headingDegrees)
        {
            if (tile == null || item == null || !instanceOf.TryGetValue(item, out var place))
                return;
            place.Key.Instances[place.Value] = PlacementMatrix(place.Key.Model, tile, localX, localY, item.Height, headingDegrees);
        }

        /// <summary>
        /// Position on the map, standing on the terrain under it unless the height is absolute, turned clockwise by the
        /// heading as OMSI does. Tree crowns sit at the tree's mean height, above the roads they may overhang.
        /// </summary>
        private Matrix4x4 PlacementMatrix(OmsiObjectModel model, OmsiTileSummary tile, double localX, double localY, double height,
            double headingDegrees)
        {
            var worldX = tile.X * tileSize + localX;
            var worldY = tile.Y * tileSize + localY;
            var ground = model.HasAbsoluteHeight ? 0.0 : GroundAt(worldX, worldY);
            var position = new Vector3((float)(worldX - originX), (float)(ground + height), (float)(worldY - originY));
            var scale = Vector3.one;
            if (model.IsTree)
            {
                position.y += (float)model.TreeHeight;
                scale = new Vector3((float)model.TreeRadius, (float)model.TreeRadius, (float)model.TreeRadius);
            }
            return Matrix4x4.TRS(position, Quaternion.Euler(0f, (float)headingDegrees, 0f), scale);
        }

        /// <summary>Terrain height at a world position, from the tile that contains it; 0 where no terrain was read.</summary>
        private double GroundAt(double worldX, double worldY)
        {
            var tileX = (int)Math.Floor(worldX / tileSize);
            var tileY = (int)Math.Floor(worldY / tileSize);
            return terrains.TryGetValue(TileKey(tileX, tileY), out var terrain)
                ? terrain.HeightAt(worldX - tileX * tileSize, worldY - tileY * tileSize, tileSize)
                : 0.0;
        }

        /// <summary>
        /// A flat dome of radius 1 and height 0.6 for tree crowns. Its normals give the crown light and shade; the texture
        /// coordinates project the crown of a side-view texture from above, north at the top of the image.
        /// </summary>
        private static Mesh CreateCrown()
        {
            const int rings = 6;
            const int segments = 24;
            const float height = 0.6f;
            var vertices = new List<Vector3> { new Vector3(0f, height, 0f) };
            var normals = new List<Vector3> { Vector3.up };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var triangles = new List<int>();
            for (var ring = 1; ring <= rings; ring++)
            {
                var radius = (float)ring / rings;
                var y = height * Mathf.Sqrt(Mathf.Max(0f, 1f - radius * radius));
                for (var segment = 0; segment < segments; segment++)
                {
                    var angle = 2f * Mathf.PI * segment / segments;
                    var x = radius * Mathf.Cos(angle);
                    var z = radius * Mathf.Sin(angle);
                    vertices.Add(new Vector3(x, y, z));
                    // The normal of the ellipsoid with semi-axes 1, height and 1.
                    normals.Add(new Vector3(x, y / (height * height), z).normalized);
                    uvs.Add(new Vector2(0.5f + 0.5f * x, 0.5f - 0.5f * z));
                }
            }
            for (var segment = 0; segment < segments; segment++)
                triangles.AddRange(new[] { 0, 1 + (segment + 1) % segments, 1 + segment });
            for (var ring = 1; ring < rings; ring++)
            {
                var inner = 1 + (ring - 1) * segments;
                var outer = 1 + ring * segments;
                for (var segment = 0; segment < segments; segment++)
                {
                    var next = (segment + 1) % segments;
                    triangles.AddRange(new[] { inner + segment, inner + next, outer + segment, inner + next, outer + next, outer + segment });
                }
            }
            var mesh = new Mesh { name = "Tree crown dome" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat disc of radius 1 facing up, for tree crowns.</summary>
        private static Mesh CreateDisc()
        {
            const int segments = 20;
            var vertices = new Vector3[segments + 1];
            var normals = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            normals[0] = Vector3.up;
            for (var index = 0; index < segments; index++)
            {
                var angle = 2f * Mathf.PI * index / segments;
                vertices[index + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                normals[index + 1] = Vector3.up;
                triangles[index * 3] = 0;
                triangles[index * 3 + 1] = index + 1;
                triangles[index * 3 + 2] = (index + 1) % segments + 1;
            }
            return new Mesh { name = "Tree crown", vertices = vertices, normals = normals, uv = new Vector2[segments + 1], triangles = triangles };
        }

        private void OnDestroy()
        {
            Clear();
            if (treeDisc != null)
                Destroy(treeDisc);
            if (treeCrown != null)
                Destroy(treeCrown);
        }
    }
}
