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
    /// Draws the textured ground of a map and the splines on it from above. Every tile is a quad on the XZ plane (x
    /// east, z north, metres relative to the map origin) with one mesh per ground texture; the splines of a tile are
    /// extruded from their .sli profiles into one mesh per texture, at their real heights. An orthographic camera looks
    /// straight down and renders into the screen rectangle of the map view, below the UI.
    /// </summary>
    public sealed class MapGroundRenderer : MonoBehaviour
    {
        /// <summary>Layer of the ground meshes; only the ground camera renders it.</summary>
        public const int GroundLayer = 8;

        /// <summary>Above any terrain; an orthographic camera keeps depth precision linear over its range.</summary>
        private const float CameraHeight = 10000f;

        /// <summary>Spline surfaces come after every ground texture; those with alpha come last.</summary>
        private const int SplineQueue = 3100;

        private static readonly Color MapBackgroundColour = new Color(0.059f, 0.071f, 0.086f, 1f);
        private static readonly Color SkyColour = new Color(0.62f, 0.72f, 0.82f, 1f);
        private const int AlphaSplineQueue = 3200;

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

            public readonly List<SplineBatch> Splines = new List<SplineBatch>();

            /// <summary>The tile's heights, or null when it has no terrain file; the tile is then flat at height 0.</summary>
            public OmsiTerrain Terrain;
        }

        /// <summary>The surfaces of all splines of a tile that share one texture, relative to the tile corner.</summary>
        private sealed class SplineBatch
        {
            public string TextureKey;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        private sealed class SplineTextureData
        {
            public bool UsesAlpha;

            /// <summary>Decoded image, or null when <see cref="Encoded"/> holds a JPEG or PNG for the engine to decode.</summary>
            public OmsiImage Image;

            public byte[] Encoded;
        }

        private sealed class GroundData
        {
            public readonly List<LayerData> Layers = new List<LayerData>();
            public readonly List<TileData> Tiles = new List<TileData>();
            public readonly Dictionary<string, SplineTextureData> SplineTextures = new Dictionary<string, SplineTextureData>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Problems = new List<string>();
            public int SplineCount;
            public int DrawnSplineCount;
        }

        private Camera groundCamera;
        private Transform groundRoot;
        private Shader shader;
        private Shader splineShader;
        private readonly Dictionary<string, Material> splineMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
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
        private bool threeDimensional;

        // Heights by tile, for placing the 3D camera on the ground.
        private readonly Dictionary<long, OmsiTerrain> terrains = new Dictionary<long, OmsiTerrain>();

        /// <summary>Raised on the main thread when the ground of a map is complete, with a short report.</summary>
        public event Action<string> Loaded;

        public bool IsLoading => loading != null || pending != null;

        /// <summary>The camera looking down on the map view; other renderers draw into it too.</summary>
        public Camera ViewCamera => groundCamera;

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
            splineShader = Resources.Load<Shader>("MapSplineSurface");
            if (splineShader == null)
                Debug.LogError("MapSplineSurface.shader was not found in Resources.");

            var cameraObject = new GameObject("Map Ground Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            groundCamera = cameraObject.AddComponent<Camera>();
            groundCamera.orthographic = true;
            groundCamera.clearFlags = CameraClearFlags.SolidColor;
            groundCamera.backgroundColor = MapBackgroundColour;
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
            // Copies, so the worker never sees a list the editor changes.
            var tiles = new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord[]>>();
            foreach (var tile in summary.Tiles)
                tiles.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineRecord[]>(tile, tile.Splines.ToArray()));
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
            splineMaterials.Clear();
            terrains.Clear();
        }

        /// <summary>
        /// Switches between the map from straight above and the 3D view. In 3D the ground writes depth, so hills hide what
        /// lies behind them; from above it does not, so roads in cuttings stay visible.
        /// </summary>
        public bool ThreeDimensional
        {
            get => threeDimensional;
            set
            {
                threeDimensional = value;
                if (layerMaterials.TryGetValue(0, out var baseMaterial))
                    baseMaterial.SetFloat("_DepthWrite", value ? 1f : 0f);
            }
        }

        /// <summary>Terrain height at map-relative metres; 0 where no terrain was read.</summary>
        public float HeightAt(Vector2 mapPosition)
        {
            if (tileSize <= 0)
                return 0f;
            var worldX = mapPosition.x + originX;
            var worldY = mapPosition.y + originY;
            var tileX = (int)Math.Floor(worldX / tileSize);
            var tileY = (int)Math.Floor(worldY / tileSize);
            return terrains.TryGetValue(TileKey(tileX, tileY), out var terrain)
                ? (float)terrain.HeightAt(worldX - tileX * tileSize, worldY - tileY * tileSize, tileSize)
                : 0f;
        }

        private static long TileKey(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        /// <summary>
        /// Places a perspective camera for the 3D view, looking at <paramref name="target"/> from
        /// <paramref name="distance"/> metres, turned by <paramref name="yawDegrees"/> clockwise from north and tilted
        /// down by <paramref name="pitchDegrees"/>.
        /// </summary>
        public void SetOrbitView(Rect screenRect, Vector3 target, float yawDegrees, float pitchDegrees, float distance)
        {
            if (groundCamera == null)
                return;
            var valid = screenRect.width >= 1f && screenRect.height >= 1f && distance > 0f;
            groundCamera.enabled = valid;
            if (!valid)
                return;
            groundCamera.orthographic = false;
            groundCamera.fieldOfView = 45f;
            groundCamera.nearClipPlane = 0.5f;
            groundCamera.farClipPlane = 20000f;
            groundCamera.backgroundColor = SkyColour;
            groundCamera.pixelRect = screenRect;
            var rotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            groundCamera.transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
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
            groundCamera.orthographic = true;
            groundCamera.nearClipPlane = 1f;
            groundCamera.farClipPlane = CameraHeight * 2f;
            groundCamera.backgroundColor = MapBackgroundColour;
            groundCamera.pixelRect = screenRect;
            groundCamera.orthographicSize = viewHeightMeters * 0.5f;
            groundCamera.transform.SetPositionAndRotation(new Vector3(center.x, CameraHeight, center.y), Quaternion.Euler(90f, 0f, 0f));
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

        private static GroundData Read(List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord[]>> tiles, IOmsiFileSource source,
            string globalConfigRelativePath, string installationRoot)
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

            var profiles = new OmsiSplineProfileCatalog(installationRoot);
            foreach (var pair in tiles)
            {
                var tile = pair.Key;
                if (!tile.IsAvailable)
                    continue;
                var tileData = new TileData { X = tile.X, Y = tile.Y };
                var terrainPath = OmsiRelativePath.Normalize(tile.FileName) + ".terrain";
                try
                {
                    if (source.FileExists(terrainPath))
                        tileData.Terrain = OmsiTerrain.Parse(source.ReadAllBytes(terrainPath));
                }
                catch (InvalidDataException exception)
                {
                    data.Problems.Add(terrainPath + ": " + exception.Message);
                }
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
                AddSplines(tileData, pair.Value, profiles, installationRoot, data);
                data.Tiles.Add(tileData);
            }
            data.Problems.AddRange(profiles.Problems);
            return data;
        }

        /// <summary>Extrudes the splines of a tile and gathers their surfaces by texture.</summary>
        private static void AddSplines(TileData tile, OmsiSplineRecord[] splines, OmsiSplineProfileCatalog profiles, string installationRoot,
            GroundData data)
        {
            var batches = new Dictionary<string, SplineBatch>(StringComparer.OrdinalIgnoreCase);
            foreach (var spline in splines)
            {
                data.SplineCount++;
                var profile = profiles.Get(spline.AssetPath);
                if (profile == null || profile.EditorOnly)
                    continue;
                var drawn = false;
                foreach (var surface in OmsiSplineSurface.Build(spline, profile))
                {
                    var texture = profile.Textures[surface.TextureIndex];
                    var key = texture.Path + (texture.UsesAlpha ? "|alpha" : string.Empty);
                    if (!data.SplineTextures.ContainsKey(key))
                        data.SplineTextures[key] = ReadSplineTexture(installationRoot, texture, data.Problems);
                    if (!batches.TryGetValue(key, out var batch))
                    {
                        batch = new SplineBatch { TextureKey = key };
                        batches.Add(key, batch);
                        tile.Splines.Add(batch);
                    }

                    var first = batch.Vertices.Count;
                    for (var vertex = 0; vertex < surface.VertexCount; vertex++)
                    {
                        batch.Vertices.Add(new Vector3((float)surface.Positions[vertex * 3], (float)surface.Positions[vertex * 3 + 1],
                            (float)surface.Positions[vertex * 3 + 2]));
                        batch.Uvs.Add(new Vector2((float)surface.Uvs[vertex * 2], (float)surface.Uvs[vertex * 2 + 1]));
                    }
                    foreach (var index in surface.Triangles)
                        batch.Triangles.Add(first + index);
                    drawn = true;
                }
                if (drawn)
                    data.DrawnSplineCount++;
            }
        }

        private static SplineTextureData ReadSplineTexture(string installationRoot, OmsiSplineTexture texture, List<string> problems)
        {
            var result = new SplineTextureData { UsesAlpha = texture.UsesAlpha };
            try
            {
                var path = OmsiRelativePath.Resolve(installationRoot, texture.Path);
                if (!File.Exists(path))
                {
                    problems.Add(texture.Path + " not found");
                    return result;
                }
                var bytes = File.ReadAllBytes(path);
                if (OmsiImageReader.TryDecode(bytes, out var image))
                    result.Image = image;
                else
                    result.Encoded = bytes;
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                problems.Add(texture.Path + ": " + exception.Message);
            }
            return result;
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
                material.SetFloat("_DepthWrite", !layer.Definition.HasMask && threeDimensional ? 1f : 0f);
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
            var report = "Ground textures ready: " + data.Layers.Count + " texture(s), " + masks + " tile mask(s), " +
                         data.DrawnSplineCount + " of " + data.SplineCount + " splines with surfaces";
            if (data.Problems.Count > 0)
                report += "; " + data.Problems.Count + " problem(s), first: " + data.Problems[0];
            if (data.Problems.Count > 0)
                Debug.LogWarning(report + "\n" + string.Join("\n", data.Problems));
            Loaded?.Invoke(report + ".");
        }

        private void BuildTile(TileData tile)
        {
            var position = new Vector3((float)(tile.X * tileSize - originX), 0f, (float)(tile.Y * tileSize - originY));
            var mesh = tileMesh;
            if (tile.Terrain != null)
            {
                terrains[TileKey(tile.X, tile.Y)] = tile.Terrain;
                mesh = CreateTerrainMesh(tile.Terrain, (float)tileSize);
                owned.Add(mesh);
            }
            if (layerMaterials.TryGetValue(0, out var baseMaterial))
                AddQuad("Tile " + tile.X + ", " + tile.Y, position, baseMaterial, mesh);
            foreach (var pair in tile.Masks)
            {
                if (!layerMaterials.TryGetValue(pair.Key, out var layerMaterial))
                    continue;
                var material = new Material(layerMaterial);
                material.SetTexture("_Mask", Upload(pair.Value, null, TextureWrapMode.Clamp));
                owned.Add(material);
                AddQuad("Tile " + tile.X + ", " + tile.Y + " layer " + pair.Key, position, material, mesh);
            }
            foreach (var batch in tile.Splines)
                AddSplineBatch("Tile " + tile.X + ", " + tile.Y + " splines", position, batch);
        }

        private void AddSplineBatch(string name, Vector3 position, SplineBatch batch)
        {
            var material = SplineMaterial(batch.TextureKey);
            if (material == null)
                return;
            var mesh = new Mesh { name = name };
            if (batch.Vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(batch.Vertices);
            mesh.SetUVs(0, batch.Uvs);
            mesh.SetTriangles(batch.Triangles, 0);
            mesh.RecalculateBounds();
            owned.Add(mesh);

            var item = new GameObject(name) { layer = GroundLayer };
            item.transform.SetParent(groundRoot, false);
            item.transform.localPosition = position;
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = item.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>One material per spline texture, shared by all tiles.</summary>
        private Material SplineMaterial(string key)
        {
            if (splineMaterials.TryGetValue(key, out var material))
                return material;
            if (splineShader == null || pending == null || !pending.SplineTextures.TryGetValue(key, out var texture))
                return null;

            material = new Material(splineShader);
            material.SetTexture("_MainTex", UploadSplineTexture(texture));
            if (texture.UsesAlpha)
            {
                material.SetFloat("_SourceBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DestinationBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_DepthWrite", 0f);
                material.SetFloat("_UseAlpha", 1f);
                material.renderQueue = AlphaSplineQueue;
            }
            else
            {
                material.renderQueue = SplineQueue;
            }
            owned.Add(material);
            splineMaterials.Add(key, material);
            return material;
        }

        private Texture2D UploadSplineTexture(SplineTextureData texture)
        {
            if (texture.Image != null)
                return Upload(texture.Image, null, TextureWrapMode.Repeat);
            if (texture.Encoded == null)
                return Texture2D.grayTexture;

            var encoded = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            if (!encoded.LoadImage(texture.Encoded, true))
            {
                Destroy(encoded);
                return Texture2D.grayTexture;
            }
            owned.Add(encoded);
            return encoded;
        }

        private void AddQuad(string name, Vector3 position, Material material, Mesh mesh)
        {
            var quad = new GameObject(name) { layer = GroundLayer };
            quad.transform.SetParent(groundRoot, false);
            quad.transform.localPosition = position;
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = quad.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// The terrain of a tile as a grid of 61 × 61 heights from the south-west corner, with the normals of its slopes;
        /// UV (0, 0) south-west, (1, 1) north-east, as for the flat tile.
        /// </summary>
        private static Mesh CreateTerrainMesh(OmsiTerrain terrain, float size)
        {
            const int points = OmsiTerrain.Resolution;
            var step = size / (points - 1);
            var vertices = new Vector3[points * points];
            var normals = new Vector3[points * points];
            var uvs = new Vector2[points * points];
            for (var row = 0; row < points; row++)
            {
                for (var column = 0; column < points; column++)
                {
                    var index = row * points + column;
                    vertices[index] = new Vector3(column * step, terrain[column, row], row * step);
                    uvs[index] = new Vector2((float)column / (points - 1), (float)row / (points - 1));
                    // Slopes from the neighbours, one-sided at the tile edge.
                    var east = terrain[Math.Min(column + 1, points - 1), row] - terrain[Math.Max(column - 1, 0), row];
                    var north = terrain[column, Math.Min(row + 1, points - 1)] - terrain[column, Math.Max(row - 1, 0)];
                    var spanX = (Math.Min(column + 1, points - 1) - Math.Max(column - 1, 0)) * step;
                    var spanZ = (Math.Min(row + 1, points - 1) - Math.Max(row - 1, 0)) * step;
                    normals[index] = new Vector3(-east / spanX, 1f, -north / spanZ).normalized;
                }
            }
            var triangles = new int[(points - 1) * (points - 1) * 6];
            var next = 0;
            for (var row = 0; row + 1 < points; row++)
            {
                for (var column = 0; column + 1 < points; column++)
                {
                    var corner = row * points + column;
                    triangles[next++] = corner;
                    triangles[next++] = corner + points + 1;
                    triangles[next++] = corner + 1;
                    triangles[next++] = corner;
                    triangles[next++] = corner + points;
                    triangles[next++] = corner + points + 1;
                }
            }
            var mesh = new Mesh { name = "Terrain tile", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A tile-sized quad from the south-west corner; UV (0, 0) south-west, (1, 1) north-east.</summary>
        private static Mesh CreateTileMesh(float size)
        {
            var mesh = new Mesh { name = "Ground tile" };
            mesh.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(size, 0f, 0f), new Vector3(size, 0f, size), new Vector3(0f, 0f, size) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
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
