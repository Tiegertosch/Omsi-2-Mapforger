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
    /// Draws the textured ground of a map from above: every tile is a quad on the XZ plane (x east, z north, metres
    /// relative to the map origin) with one mesh per ground texture. An orthographic camera looks straight down and
    /// renders into the screen rectangle of the map view, below the UI.
    /// </summary>
    public sealed class MapGroundRenderer : MonoBehaviour
    {
        /// <summary>Layer of the ground meshes; only the ground camera renders it.</summary>
        public const int GroundLayer = 8;

        private const float CameraHeight = 500f;

        /// <summary>Texture uploads per frame stop after this many milliseconds, so the UI stays responsive.</summary>
        private const float UploadBudgetMilliseconds = 8f;

        private sealed class LayerData
        {
            public OmsiGroundTexture Definition;
            public OmsiImage Texture;
            public OmsiImage Detail;
        }

        private sealed class TileData
        {
            public int X;
            public int Y;

            /// <summary>Masks by ground texture index; the first texture has none.</summary>
            public readonly List<KeyValuePair<int, OmsiImage>> Masks = new List<KeyValuePair<int, OmsiImage>>();
        }

        private sealed class GroundData
        {
            public readonly List<LayerData> Layers = new List<LayerData>();
            public readonly List<TileData> Tiles = new List<TileData>();
            public readonly List<string> Problems = new List<string>();
        }

        private Camera groundCamera;
        private Transform groundRoot;
        private Shader shader;
        private Mesh tileMesh;
        private double tileSize;
        private double originX;
        private double originY;
        // Counts maps; a read that finishes after another map was opened is dropped.
        private int generation;
        private int loadingGeneration;
        private Task<GroundData> loading;
        private GroundData pending;
        private int pendingTile;
        private readonly Dictionary<int, Material> layerMaterials = new Dictionary<int, Material>();
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private bool groundVisible = true;

        /// <summary>Raised on the main thread when the ground of a map is complete, with a short report.</summary>
        public event Action<string> Loaded;

        public bool IsLoading => loading != null || pending != null;

        public bool GroundVisible
        {
            get => groundVisible;
            set
            {
                groundVisible = value;
                if (groundRoot != null)
                    groundRoot.gameObject.SetActive(value);
            }
        }

        private void Awake()
        {
            shader = Resources.Load<Shader>("MapGroundLayer");
            if (shader == null)
                Debug.LogError("MapGroundLayer.shader was not found in Resources.");

            var cameraObject = new GameObject("Map Ground Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            groundCamera = cameraObject.AddComponent<Camera>();
            groundCamera.orthographic = true;
            groundCamera.clearFlags = CameraClearFlags.SolidColor;
            groundCamera.backgroundColor = new Color(0.059f, 0.071f, 0.086f, 1f);
            groundCamera.cullingMask = 1 << GroundLayer;
            groundCamera.depth = -50f;
            groundCamera.nearClipPlane = 1f;
            groundCamera.farClipPlane = CameraHeight * 2f;
            groundCamera.useOcclusionCulling = false;
            groundCamera.enabled = false;
        }

        /// <summary>
        /// Reads the ground textures and tile masks of a map in the background and builds the ground once they are
        /// decoded. Map files come through <paramref name="source"/>, so a workspace's edited masks count; textures
        /// come from the OMSI installation.
        /// </summary>
        /// <param name="originX">Map origin in world metres, the same the map view draws relative to.</param>
        public void Load(OmsiMapSummary summary, IOmsiFileSource source, string globalConfigRelativePath, string installationRoot,
            double originX, double originY)
        {
            Clear();
            if (summary == null || source == null)
                return;

            tileSize = summary.TileSizeMeters;
            this.originX = originX;
            this.originY = originY;
            var tiles = new List<OmsiTileSummary>(summary.Tiles);
            loadingGeneration = generation;
            loading = Task.Run(() => Read(tiles, source, globalConfigRelativePath, installationRoot));
        }

        /// <summary>Removes the ground of the previous map.</summary>
        public void Clear()
        {
            generation++;
            loading = null;
            pending = null;
            pendingTile = 0;
            if (groundRoot != null)
                Destroy(groundRoot.gameObject);
            groundRoot = null;
            foreach (var item in owned)
                Destroy(item);
            owned.Clear();
            layerMaterials.Clear();
        }

        /// <summary>
        /// Places the camera so that the ground lines up with the map view.
        /// </summary>
        /// <param name="screenRect">The map view in screen pixels, origin at the bottom left.</param>
        /// <param name="center">Map-relative metres at the centre of the view.</param>
        /// <param name="viewHeightMeters">Metres shown from the bottom to the top of the view.</param>
        public void SetView(Rect screenRect, Vector2 center, float viewHeightMeters)
        {
            if (groundCamera == null)
                return;
            var valid = screenRect.width >= 1f && screenRect.height >= 1f && viewHeightMeters > 0f;
            groundCamera.enabled = valid;
            if (!valid)
                return;
            groundCamera.pixelRect = screenRect;
            groundCamera.orthographicSize = viewHeightMeters * 0.5f;
            groundCamera.transform.position = new Vector3(center.x, CameraHeight, center.y);
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
                    var message = task.Exception?.GetBaseException().Message;
                    Debug.LogException(task.Exception);
                    Loaded?.Invoke("Ground textures could not be read: " + message);
                    return;
                }
                pending = task.Result;
                pendingTile = 0;
                BeginBuild(pending);
            }

            if (pending != null)
                ContinueBuild();
        }

        private static GroundData Read(List<OmsiTileSummary> tiles, IOmsiFileSource source, string globalConfigRelativePath, string installationRoot)
        {
            var data = new GroundData();
            var images = new Dictionary<string, OmsiImage>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in OmsiGroundTextures.Read(source, globalConfigRelativePath))
            {
                data.Layers.Add(new LayerData
                {
                    Definition = definition,
                    Texture = ReadInstallationImage(installationRoot, definition.TexturePath, images, data.Problems),
                    Detail = ReadInstallationImage(installationRoot, definition.DetailTexturePath, images, data.Problems)
                });
            }

            foreach (var tile in tiles)
            {
                if (!tile.IsAvailable)
                    continue;
                var tileData = new TileData { X = tile.X, Y = tile.Y };
                foreach (var layer in data.Layers)
                {
                    if (!layer.Definition.HasMask)
                        continue;
                    var maskPath = layer.Definition.MaskPath(tile.FileName);
                    if (!source.FileExists(maskPath))
                        continue;
                    try
                    {
                        if (OmsiImageReader.TryDecode(source.ReadAllBytes(maskPath), out var mask))
                            tileData.Masks.Add(new KeyValuePair<int, OmsiImage>(layer.Definition.Index, mask));
                    }
                    catch (InvalidDataException exception)
                    {
                        data.Problems.Add(maskPath + ": " + exception.Message);
                    }
                }
                data.Tiles.Add(tileData);
            }
            return data;
        }

        private static OmsiImage ReadInstallationImage(string installationRoot, string relativePath, Dictionary<string, OmsiImage> cache,
            List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;
            if (cache.TryGetValue(relativePath, out var cached))
                return cached;

            OmsiImage image = null;
            try
            {
                var path = OmsiRelativePath.Resolve(installationRoot, relativePath);
                if (!File.Exists(path))
                    problems.Add(relativePath + " not found");
                else if (!OmsiImageReader.TryDecode(File.ReadAllBytes(path), out image))
                    problems.Add(relativePath + " has an unsupported format");
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                problems.Add(relativePath + ": " + exception.Message);
            }
            cache[relativePath] = image;
            return image;
        }

        private void BeginBuild(GroundData data)
        {
            groundRoot = new GameObject("Ground").transform;
            groundRoot.SetParent(transform, false);
            groundRoot.gameObject.SetActive(groundVisible);
            tileMesh = CreateTileMesh((float)tileSize);
            owned.Add(tileMesh);

            var textures = new Dictionary<OmsiImage, Texture2D>();
            foreach (var layer in data.Layers)
            {
                if (shader == null)
                    break;
                var material = new Material(shader) { renderQueue = 3000 + layer.Definition.Index };
                material.SetTexture("_MainTex", Upload(layer.Texture, textures, TextureWrapMode.Repeat));
                material.SetTexture("_DetailTex", Upload(layer.Detail, textures, TextureWrapMode.Repeat));
                material.SetFloat("_Repeats", (float)layer.Definition.Repeats);
                material.SetFloat("_DetailRepeats", (float)layer.Definition.DetailRepeats);
                material.SetFloat("_UseMask", layer.Definition.HasMask ? 1f : 0f);
                owned.Add(material);
                layerMaterials[layer.Definition.Index] = material;
            }
        }

        /// <summary>Creates the tiles a few at a time; mask uploads are the expensive part.</summary>
        private void ContinueBuild()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (pendingTile < pending.Tiles.Count && watch.Elapsed.TotalMilliseconds < UploadBudgetMilliseconds)
                BuildTile(pending.Tiles[pendingTile++]);
            if (pendingTile < pending.Tiles.Count)
                return;

            var data = pending;
            pending = null;
            var masks = 0;
            foreach (var tile in data.Tiles)
                masks += tile.Masks.Count;
            var report = "Ground textures ready: " + data.Layers.Count + " texture(s), " + masks + " tile mask(s)";
            if (data.Problems.Count > 0)
                report += "; " + data.Problems.Count + " problem(s), first: " + data.Problems[0];
            if (data.Problems.Count > 0)
                Debug.LogWarning(report + "\n" + string.Join("\n", data.Problems));
            Loaded?.Invoke(report + ".");
        }

        private void BuildTile(TileData tile)
        {
            var position = new Vector3((float)(tile.X * tileSize - originX), 0f, (float)(tile.Y * tileSize - originY));
            if (layerMaterials.TryGetValue(0, out var baseMaterial))
                AddQuad("Tile " + tile.X + ", " + tile.Y, position, baseMaterial);
            foreach (var pair in tile.Masks)
            {
                if (!layerMaterials.TryGetValue(pair.Key, out var layerMaterial))
                    continue;
                var material = new Material(layerMaterial);
                material.SetTexture("_Mask", Upload(pair.Value, null, TextureWrapMode.Clamp));
                owned.Add(material);
                AddQuad("Tile " + tile.X + ", " + tile.Y + " layer " + pair.Key, position, material);
            }
        }

        private void AddQuad(string name, Vector3 position, Material material)
        {
            var quad = new GameObject(name) { layer = GroundLayer };
            quad.transform.SetParent(groundRoot, false);
            quad.transform.localPosition = position;
            quad.AddComponent<MeshFilter>().sharedMesh = tileMesh;
            var meshRenderer = quad.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>A tile-sized quad from the south-west corner; UV (0, 0) south-west, (1, 1) north-east.</summary>
        private static Mesh CreateTileMesh(float size)
        {
            var mesh = new Mesh { name = "Ground tile" };
            mesh.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(size, 0f, 0f), new Vector3(size, 0f, size), new Vector3(0f, 0f, size) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Creates a texture with mipmaps. OMSI images run from the top row down; Unity expects the bottom row first,
        /// so the rows are reversed and the top row ends up at the north side.
        /// </summary>
        private Texture2D Upload(OmsiImage image, Dictionary<OmsiImage, Texture2D> shared, TextureWrapMode wrap)
        {
            if (image == null)
                return Texture2D.whiteTexture;
            if (shared != null && shared.TryGetValue(image, out var existing))
                return existing;

            var format = image.Channels == 1 ? TextureFormat.R8 : TextureFormat.RGBA32;
            var texture = new Texture2D(image.Width, image.Height, format, true)
            {
                wrapMode = wrap,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var rowBytes = image.Width * image.Channels;
            var flipped = new byte[image.Pixels.Length];
            for (var row = 0; row < image.Height; row++)
                Buffer.BlockCopy(image.Pixels, row * rowBytes, flipped, (image.Height - 1 - row) * rowBytes, rowBytes);
            texture.SetPixelData(flipped, 0);
            texture.Apply(true, true);
            owned.Add(texture);
            if (shared != null)
                shared[image] = texture;
            return texture;
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
