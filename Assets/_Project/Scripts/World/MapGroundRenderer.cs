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
    /// extruded from their .sli profiles into one mesh per texture, at their real heights. Where OMSI cuts the terrain
    /// away, under crossings with a terrain hole and along spline profiles with one, a hole mask per tile leaves the
    /// ground out. An orthographic camera looks straight down and renders into the screen rectangle of the map view,
    /// below the UI. The masks of the ground textures are read only for tiles near the view and dropped again for tiles
    /// long out of it, so large maps load fast and keep a bounded amount of them in memory.
    /// </summary>
    public sealed class MapGroundRenderer : MonoBehaviour
    {
        /// <summary>Layer of the ground meshes; only the ground camera renders it.</summary>
        public const int GroundLayer = 8;

        /// <summary>Vertical field of view of the 3D view in degrees; the map view projects with the same value.</summary>
        public const float OrbitFieldOfView = 45f;

        /// <summary>Above any terrain; an orthographic camera keeps depth precision linear over its range.</summary>
        private const float CameraHeight = 10000f;

        /// <summary>Spline surfaces come after every ground texture; those with alpha come last.</summary>
        private const int SplineQueue = 3100;

        private static readonly Color MapBackgroundColour = new Color(0.059f, 0.071f, 0.086f, 1f);
        private static readonly Color SkyColour = new Color(0.62f, 0.72f, 0.82f, 1f);
        private const int AlphaSplineQueue = 3200;

        /// <summary>Texture uploads per frame stop after this many milliseconds, so the UI stays responsive.</summary>
        private const float UploadBudgetMilliseconds = 8f;

        /// <summary>At most this many tiles keep their masks; the ones out of view longest are dropped first.</summary>
        private const int MaximumMaskTiles = 96;

        /// <summary>From above, masks are read only while the view shows at most this many metres from top to bottom.</summary>
        private const float MaskViewMeters = 3000f;

        /// <summary>Tiles a decode task reads at once, so nearby tiles show their masks soon.</summary>
        private const int MaskTilesPerTask = 12;

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

            /// <summary>Mask files by ground texture index, read when the tile comes into view; the first texture has none.</summary>
            public readonly List<KeyValuePair<int, string>> MaskPaths = new List<KeyValuePair<int, string>>();

            public readonly List<SplineBatch> Splines = new List<SplineBatch>();

            /// <summary>The tile's heights, or null when it has no terrain file; the tile is then flat at height 0.</summary>
            public OmsiTerrain Terrain;

            /// <summary>Where the terrain is cut away, or null when the tile has no hole.</summary>
            public OmsiTerrainHoleMask Holes;
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
            public int HoleTileCount;
            public HoleSources Holes;

            /// <summary>Spline types without a surface from above that show as lines, by asset path.</summary>
            public readonly Dictionary<string, OmsiSplineLineKind> LineKinds = new Dictionary<string, OmsiSplineLineKind>(StringComparer.OrdinalIgnoreCase);
            public int LineSplineCount;
        }

        /// <summary>A built tile: where it is, its ground mesh, and the mask layers it has while it is near the view.</summary>
        private sealed class TileState
        {
            public long Key;
            public int X;
            public int Y;
            public Vector3 Position;
            public Mesh Mesh;
            public List<KeyValuePair<int, string>> MaskPaths;
            public readonly List<GameObject> MaskLayers = new List<GameObject>();
            public readonly List<UnityEngine.Object> MaskResources = new List<UnityEngine.Object>();
            public bool MasksLoaded;
            public bool MasksLoading;
            public float LastWanted;
        }

        /// <summary>Decoded masks of a tile, waiting to be uploaded.</summary>
        private sealed class DecodedMasks
        {
            public long Key;
            public readonly List<KeyValuePair<int, OmsiImage>> Masks = new List<KeyValuePair<int, OmsiImage>>();
        }

        private readonly Dictionary<long, TileState> tileStates = new Dictionary<long, TileState>();
        private readonly Queue<DecodedMasks> decodedMasks = new Queue<DecodedMasks>();
        private Task<List<DecodedMasks>> maskTask;
        private IOmsiFileSource maskSource;
        private float nextMaskCheck;
        private readonly List<string> maskProblems = new List<string>();

        // The last view, for choosing the tiles whose masks are read: map-relative centre, and either the size seen
        // from above or the 3D camera's distance.
        private Vector2 viewCenter;
        private Vector2 viewHalfSize;
        private float orbitDistance;
        private bool viewIsOrbit;
        private bool hasViewForMasks;

        private Camera groundCamera;
        private Transform groundRoot;
        private Shader shader;
        private Shader splineShader;

        // The water surfaces of tiles with [water], one quad each at their corner heights.
        private Transform waterRoot;
        private readonly List<UnityEngine.Object> waterResources = new List<UnityEngine.Object>();

        // The map whose water is drawn, drawn again once the ground's terrain is loaded.
        private OmsiMapSummary waterSummary;

        // Pictures from the air laid over each tile's ground, drawn over the ground textures and under the splines, with
        // one opacity for all. The pictures outlive a new load of the same map; their surfaces follow the ground.
        private readonly Dictionary<long, Texture2D> imageryTextures = new Dictionary<long, Texture2D>();
        private readonly Dictionary<long, MeshRenderer> imagerySurfaces = new Dictionary<long, MeshRenderer>();

        // Tiles whose picture lies less opaque than the others or is hidden (0), as a factor on the opacity.
        private readonly Dictionary<long, float> imageryTileOpacity = new Dictionary<long, float>();

        // Full pictures of tiles near the view while zoomed in, shown in place of their smaller copy.
        private readonly Dictionary<long, Texture2D> sharpTextures = new Dictionary<long, Texture2D>();
        private Transform imageryRoot;
        private float imageryOpacity = 1f;
        private bool imageryVisible = true;
        private const int ImageryQueue = 3090;
        private readonly Dictionary<string, Material> splineMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        // The spline meshes of each tile, so changed splines can be built again without reading the whole map.
        private readonly Dictionary<long, List<GameObject>> tileSplineObjects = new Dictionary<long, List<GameObject>>();

        // Tiles changed while the ground was still loading; their splines and terrain are built again once it is done,
        // since the load read them before the change.
        private readonly List<OmsiTileSummary> queuedSplineTiles = new List<OmsiTileSummary>();
        private readonly List<OmsiTileSummary> queuedTerrainTiles = new List<OmsiTileSummary>();
        private string loadedInstallationRoot;
        private OmsiSplineProfileCatalog refreshProfiles;
        private readonly Dictionary<string, SplineTextureData> refreshTextures = new Dictionary<string, SplineTextureData>(StringComparer.OrdinalIgnoreCase);
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

        // Copies of the base material for tiles with terrain holes.
        private readonly List<Material> holeBaseMaterials = new List<Material>();

        /// <summary>The renderers of each tile's ground layers, by tile key, so holes can be cut again after edits.</summary>
        private readonly Dictionary<long, List<MeshRenderer>> tileMaterials = new Dictionary<long, List<MeshRenderer>>();
        private readonly Dictionary<long, Texture2D> holeTextures = new Dictionary<long, Texture2D>();

        /// <summary>What the holes of the open map were built from; null until the ground is read.</summary>
        private HoleSources holes;

        private sealed class HoleSources
        {
            public OmsiTerrainHoleCatalog Catalog;
            public OmsiSplineProfileCatalog Profiles;
            public List<KeyValuePair<long, OmsiSplineRecord>> Splines;
            public HashSet<long> Tiles;
            public string Signature;
        }
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private bool groundVisible = true;
        private bool threeDimensional;

        // Heights by tile, for placing the 3D camera on the ground.
        private readonly Dictionary<long, OmsiTerrain> terrains = new Dictionary<long, OmsiTerrain>();

        /// <summary>Raised on the main thread when the ground of a map is complete, with a short report.</summary>
        public event Action<string> Loaded;

        /// <summary>
        /// Spline types of the open map that have no surface seen from above, such as overhead lines and fences, by asset
        /// path; empty until the ground is read.
        /// </summary>
        public IReadOnlyDictionary<string, OmsiSplineLineKind> SplineLineKinds { get; private set; } =
            new Dictionary<string, OmsiSplineLineKind>();

        /// <summary>
        /// True while the map's ground is read or built, and while masks of tiles in view are still read or uploaded, so
        /// a screenshot waits for them.
        /// </summary>
        public bool IsLoading => loading != null || pending != null || maskTask != null || decodedMasks.Count > 0 || MasksWanted();

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
            loadedInstallationRoot = installationRoot;
            this.originX = originX;
            this.originY = originY;
            maskSource = source;
            // Copies, so the worker never sees a list the editor changes.
            var tiles = new List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord[]>>();
            foreach (var tile in summary.Tiles)
                tiles.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineRecord[]>(tile, tile.Splines.ToArray()));
            var placements = Placements(summary);
            loadingGeneration = generation;
            var size = tileSize;
            loading = Task.Run(() => Read(tiles, placements, source, globalConfigRelativePath, installationRoot, size));
        }

        private static List<OmsiHolePlacement> Placements(OmsiMapSummary summary)
        {
            var placements = new List<OmsiHolePlacement>();
            foreach (var tile in summary.Tiles)
                foreach (var item in tile.Objects)
                    placements.Add(new OmsiHolePlacement
                    {
                        TileX = tile.X, TileY = tile.Y, AssetPath = item.AssetPath, LocalX = item.LocalX, LocalY = item.LocalY,
                        HeadingDegrees = item.HeadingDegrees, PitchDegrees = item.PitchDegrees, BankDegrees = item.BankDegrees
                    });
            return placements;
        }

        /// <summary>
        /// Cuts the terrain holes again after objects were moved, turned, placed or deleted, if any object with a terrain
        /// hole changed; other edits cost a pass over the objects only.
        /// </summary>
        public void RefreshTerrainHoles(OmsiMapSummary summary)
        {
            if (holes == null || summary == null || loading != null || pending != null)
                return;
            var placements = Placements(summary);
            // Splines built since loading, such as rail cuttings, cut the ground as well.
            var splines = new List<KeyValuePair<long, OmsiSplineRecord>>();
            var ids = new System.Text.StringBuilder();
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                {
                    splines.Add(new KeyValuePair<long, OmsiSplineRecord>(OmsiTerrainHoleCatalog.TileKey(tile.X, tile.Y), spline));
                    var profile = holes.Profiles.Get(spline.AssetPath);
                    if (profile != null && (profile.TerrainHoles.Count > 0 || spline.AlignsTerrain))
                        ids.Append(spline.Id).Append(',');
                }
            var signature = HoleSignature(holes.Catalog, placements) + "|" + ids;
            if (signature == holes.Signature)
                return;
            holes.Signature = signature;
            holes.Splines = splines;
            var masks = holes.Catalog.Build(placements, holes.Splines, holes.Profiles, holes.Tiles, tileSize);
            foreach (var pair in tileMaterials)
            {
                masks.TryGetValue(pair.Key, out var mask);
                if (mask != null && mask.HolePixelCount == 0)
                    mask = null;
                ApplyHoles(pair.Key, pair.Value, mask);
            }
        }

        /// <summary>Where the objects with terrain holes stand, as text; equal text means equal holes.</summary>
        private static string HoleSignature(OmsiTerrainHoleCatalog catalog, List<OmsiHolePlacement> placements)
        {
            var text = new System.Text.StringBuilder();
            foreach (var placement in placements)
            {
                if (catalog.ObjectHole(placement.AssetPath) == null)
                    continue;
                text.Append(placement.TileX).Append(',').Append(placement.TileY).Append(',').Append(placement.AssetPath).Append(',')
                    .Append(placement.LocalX.ToString("R")).Append(',').Append(placement.LocalY.ToString("R")).Append(',')
                    .Append(placement.HeadingDegrees.ToString("R")).Append(',').Append(placement.PitchDegrees.ToString("R")).Append(',')
                    .Append(placement.BankDegrees.ToString("R")).Append(';');
            }
            return text.ToString();
        }

        /// <summary>Gives the materials of a tile a hole mask, a new one, or none.</summary>
        private void ApplyHoles(long key, List<MeshRenderer> renderers, OmsiTerrainHoleMask mask)
        {
            if (holeTextures.TryGetValue(key, out var old))
            {
                holeTextures.Remove(key);
                owned.Remove(old);
                Destroy(old);
            }
            var texture = mask != null ? UploadHoles(mask) : null;
            if (texture != null)
                holeTextures[key] = texture;
            foreach (var meshRenderer in renderers)
            {
                var material = meshRenderer.sharedMaterial;
                // The base material is shared by all tiles; a tile that gets holes needs its own copy.
                if (texture != null && layerMaterials.TryGetValue(0, out var baseMaterial) && material == baseMaterial)
                {
                    material = new Material(baseMaterial);
                    owned.Add(material);
                    holeBaseMaterials.Add(material);
                    meshRenderer.sharedMaterial = material;
                }
                if (texture != null)
                    SetHoles(material, texture);
                else
                    material.SetFloat("_UseHoles", 0f);
            }
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
            foreach (var key in new List<long>(imagerySurfaces.Keys))
                RemoveImagerySurface(key);
            foreach (var item in owned)
                Destroy(item);
            owned.Clear();
            layerMaterials.Clear();
            holeBaseMaterials.Clear();
            tileMaterials.Clear();
            holeTextures.Clear();
            holes = null;
            foreach (var state in tileStates.Values)
                foreach (var resource in state.MaskResources)
                    Destroy(resource);
            tileStates.Clear();
            decodedMasks.Clear();
            maskTask = null;
            maskSource = null;
            maskProblems.Clear();
            SplineLineKinds = new Dictionary<string, OmsiSplineLineKind>();
            splineMaterials.Clear();
            terrains.Clear();
            TerrainVersion++;
            tileSplineObjects.Clear();
            queuedSplineTiles.Clear();
            queuedTerrainTiles.Clear();
            refreshProfiles = null;
            refreshTextures.Clear();
        }

        private static void Queue(List<OmsiTileSummary> queue, IEnumerable<OmsiTileSummary> tiles)
        {
            foreach (var tile in tiles)
                if (tile != null && !queue.Contains(tile))
                    queue.Add(tile);
        }

        /// <summary>Builds what changed while the ground was loading.</summary>
        private void RunQueuedRefreshes()
        {
            if (queuedTerrainTiles.Count > 0)
            {
                var terrain = new List<OmsiTileSummary>(queuedTerrainTiles);
                queuedTerrainTiles.Clear();
                RefreshTerrain(terrain);
            }
            if (queuedSplineTiles.Count > 0)
            {
                var splines = new List<OmsiTileSummary>(queuedSplineTiles);
                queuedSplineTiles.Clear();
                RefreshSplines(splines);
            }
        }

        /// <summary>
        /// Reads the terrain of some tiles again, after roads fitted it, and gives their ground and mask layers the new
        /// surface. Holes follow with <see cref="RefreshTerrainHoles"/>.
        /// </summary>
        public void RefreshTerrain(IEnumerable<OmsiTileSummary> tiles)
        {
            if (tiles == null)
                return;
            if (groundRoot == null || maskSource == null || loading != null || pending != null)
            {
                Queue(queuedTerrainTiles, tiles);
                return;
            }
            foreach (var tile in tiles)
            {
                var key = TileKey(tile.X, tile.Y);
                var path = OmsiRelativePath.Normalize(tile.FileName) + ".terrain";
                if (!tileStates.TryGetValue(key, out var state) || !maskSource.FileExists(path))
                    continue;
                OmsiTerrain terrain;
                try
                {
                    terrain = OmsiTerrain.Parse(maskSource.ReadAllBytes(path));
                }
                catch (InvalidDataException exception)
                {
                    Debug.LogWarning(path + ": " + exception.Message);
                    continue;
                }
                terrains[key] = terrain;
                TerrainVersion++;
                var mesh = CreateTerrainMesh(terrain, (float)tileSize);
                owned.Add(mesh);
                if (tileMaterials.TryGetValue(key, out var renderers))
                    foreach (var meshRenderer in renderers)
                        if (meshRenderer != null)
                            meshRenderer.GetComponent<MeshFilter>().sharedMesh = mesh;
                foreach (var layer in state.MaskLayers)
                    if (layer != null)
                        layer.GetComponent<MeshFilter>().sharedMesh = mesh;
                if (imagerySurfaces.TryGetValue(key, out var overlay) && overlay != null)
                    overlay.GetComponent<MeshFilter>().sharedMesh = mesh;
                if (state.Mesh != null && state.Mesh != tileMesh)
                {
                    owned.Remove(state.Mesh);
                    Destroy(state.Mesh);
                }
                state.Mesh = mesh;
            }
        }

        /// <summary>
        /// Reads the spline profiles again and builds the surfaces of some tiles anew, after a road type they use was
        /// changed (ADR 049).
        /// </summary>
        public bool RefreshProfiles(IEnumerable<OmsiTileSummary> tiles)
        {
            refreshProfiles = null;
            return RefreshSplines(tiles);
        }

        /// <summary>
        /// Builds the spline surfaces of some tiles again from their current splines, after roads were built, deleted,
        /// undone or redone. Other tiles stay as they are. While the map is still loading, the load reads the new state.
        /// </summary>
        /// <returns>False when the ground is still loading; the tiles are then built again once it is done.</returns>
        public bool RefreshSplines(IEnumerable<OmsiTileSummary> tiles)
        {
            if (tiles == null)
                return true;
            if (groundRoot == null || loading != null || pending != null || string.IsNullOrEmpty(loadedInstallationRoot))
            {
                Queue(queuedSplineTiles, tiles);
                return false;
            }
            refreshProfiles = refreshProfiles ?? new OmsiSplineProfileCatalog(loadedInstallationRoot);
            var data = new GroundData();
            foreach (var pair in refreshTextures)
                data.SplineTextures[pair.Key] = pair.Value;
            var kinds = new Dictionary<string, OmsiSplineLineKind>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in SplineLineKinds)
                kinds[pair.Key] = pair.Value;
            foreach (var tile in tiles)
            {
                if (tile == null || !tile.IsAvailable)
                    continue;
                var key = TileKey(tile.X, tile.Y);
                if (tileSplineObjects.TryGetValue(key, out var objects))
                {
                    foreach (var item in objects)
                    {
                        var mesh = item.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh != null)
                        {
                            owned.Remove(mesh);
                            Destroy(mesh);
                        }
                        Destroy(item);
                    }
                    objects.Clear();
                }
                var tileData = new TileData { X = tile.X, Y = tile.Y };
                AddSplines(tileData, tile.Splines.ToArray(), refreshProfiles, loadedInstallationRoot, data);
                var position = new Vector3((float)(tile.X * tileSize - originX), 0f, (float)(tile.Y * tileSize - originY));
                foreach (var batch in tileData.Splines)
                    AddSplineBatch("Tile " + tile.X + ", " + tile.Y + " splines", position, batch, data.SplineTextures, key);
            }
            foreach (var pair in data.SplineTextures)
                refreshTextures[pair.Key] = pair.Value;
            foreach (var pair in data.LineKinds)
                kinds[pair.Key] = pair.Value;
            SplineLineKinds = kinds;
            if (data.Problems.Count > 0 || refreshProfiles.Problems.Count > 0)
                Debug.LogWarning("Splines drawn again with problems:\n" + string.Join("\n", data.Problems) + "\n" + string.Join("\n", refreshProfiles.Problems));
            return true;
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
                foreach (var material in holeBaseMaterials)
                    material.SetFloat("_DepthWrite", value ? 1f : 0f);
            }
        }

        /// <summary>Counts the changes to the terrain <see cref="HeightAt"/> reads, so a view keeping heights knows when to take them anew.</summary>
        public int TerrainVersion { get; private set; }

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
            groundCamera.fieldOfView = OrbitFieldOfView;
            groundCamera.nearClipPlane = 0.5f;
            groundCamera.farClipPlane = 20000f;
            groundCamera.backgroundColor = SkyColour;
            groundCamera.pixelRect = screenRect;
            viewCenter = new Vector2(target.x, target.z);
            orbitDistance = distance;
            viewIsOrbit = true;
            hasViewForMasks = true;
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
            viewCenter = center;
            viewHalfSize = new Vector2(viewHeightMeters * 0.5f * screenRect.width / screenRect.height, viewHeightMeters * 0.5f);
            viewIsOrbit = false;
            hasViewForMasks = true;
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
                holes = pending.Holes;
                SplineLineKinds = pending.LineKinds;
                BeginBuild(pending);
            }

            if (pending != null)
                ContinueBuild();
            else
                UpdateMasks();
        }

        /// <summary>
        /// Reads the masks of tiles near the view in the background, uploads finished ones within the frame budget and
        /// drops masks of tiles long out of view once too many are loaded.
        /// </summary>
        private void UpdateMasks()
        {
            if (maskTask != null && maskTask.IsCompleted)
            {
                var task = maskTask;
                maskTask = null;
                if (task.IsFaulted)
                    Debug.LogException(task.Exception);
                else
                    foreach (var decoded in task.Result)
                        decodedMasks.Enqueue(decoded);
                lock (maskProblems)
                {
                    if (maskProblems.Count > 0)
                        Debug.LogWarning("Ground masks could not be read:\n" + string.Join("\n", maskProblems));
                    maskProblems.Clear();
                }
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (decodedMasks.Count > 0 && watch.Elapsed.TotalMilliseconds < UploadBudgetMilliseconds)
                AddMaskLayers(decodedMasks.Dequeue());

            if (Time.unscaledTime < nextMaskCheck || maskTask != null || maskSource == null)
                return;
            nextMaskCheck = Time.unscaledTime + 0.2f;
            var wanted = WantedMaskTiles();
            foreach (var state in wanted)
                state.LastWanted = Time.unscaledTime;
            var toRead = wanted.FindAll(state => !state.MasksLoaded && !state.MasksLoading && state.MaskPaths.Count > 0);
            if (toRead.Count > MaskTilesPerTask)
                toRead.RemoveRange(MaskTilesPerTask, toRead.Count - MaskTilesPerTask);
            if (toRead.Count > 0)
            {
                var requests = new List<KeyValuePair<long, List<KeyValuePair<int, string>>>>();
                foreach (var state in toRead)
                {
                    state.MasksLoading = true;
                    requests.Add(new KeyValuePair<long, List<KeyValuePair<int, string>>>(state.Key, state.MaskPaths));
                }
                var source = maskSource;
                var problems = maskProblems;
                maskTask = Task.Run(() => DecodeMasks(requests, source, problems));
            }
            DropFarMasks(wanted);
        }

        /// <summary>True when a tile in view still waits for its masks.</summary>
        private bool MasksWanted()
        {
            if (maskSource == null || pending != null)
                return false;
            foreach (var state in WantedMaskTiles())
                if (!state.MasksLoaded && state.MaskPaths.Count > 0)
                    return true;
            return false;
        }

        /// <summary>
        /// Tiles near the view, nearest first and at most <see cref="MaximumMaskTiles"/>: from above those the view shows
        /// plus a tile around it, unless the view is so far out that masks would not show; in 3D those within a few times
        /// the camera's distance of the point it looks at.
        /// </summary>
        private List<TileState> WantedMaskTiles()
        {
            var wanted = new List<TileState>();
            if (!hasViewForMasks || tileStates.Count == 0 || !groundVisible)
                return wanted;
            var size = (float)tileSize;
            float halfWidth, halfHeight;
            if (viewIsOrbit)
            {
                halfWidth = halfHeight = Mathf.Min(orbitDistance * 4f, 4000f) + size;
            }
            else
            {
                if (viewHalfSize.y * 2f > MaskViewMeters)
                    return wanted;
                halfWidth = viewHalfSize.x + size;
                halfHeight = viewHalfSize.y + size;
            }
            foreach (var state in tileStates.Values)
            {
                var centerX = state.Position.x + size * 0.5f;
                var centerY = state.Position.z + size * 0.5f;
                if (Mathf.Abs(centerX - viewCenter.x) <= halfWidth + size * 0.5f && Mathf.Abs(centerY - viewCenter.y) <= halfHeight + size * 0.5f)
                    wanted.Add(state);
            }
            wanted.Sort((first, second) => DistanceToView(first, size).CompareTo(DistanceToView(second, size)));
            if (wanted.Count > MaximumMaskTiles)
                wanted.RemoveRange(MaximumMaskTiles, wanted.Count - MaximumMaskTiles);
            return wanted;
        }

        private float DistanceToView(TileState state, float size)
        {
            return (new Vector2(state.Position.x + size * 0.5f, state.Position.z + size * 0.5f) - viewCenter).sqrMagnitude;
        }

        /// <summary>Drops the masks of tiles out of view, those out of it longest first, until at most the maximum remain.</summary>
        private void DropFarMasks(List<TileState> wanted)
        {
            var loaded = new List<TileState>();
            foreach (var state in tileStates.Values)
                if (state.MasksLoaded)
                    loaded.Add(state);
            if (loaded.Count <= MaximumMaskTiles)
                return;
            var keep = new HashSet<TileState>(wanted);
            loaded.RemoveAll(keep.Contains);
            loaded.Sort((first, second) => first.LastWanted.CompareTo(second.LastWanted));
            var excess = loaded.Count + keep.Count - MaximumMaskTiles;
            for (var index = 0; index < excess && index < loaded.Count; index++)
                RemoveMaskLayers(loaded[index]);
        }

        private static List<DecodedMasks> DecodeMasks(List<KeyValuePair<long, List<KeyValuePair<int, string>>>> requests, IOmsiFileSource source,
            List<string> problems)
        {
            var result = new List<DecodedMasks>();
            foreach (var request in requests)
            {
                var decoded = new DecodedMasks { Key = request.Key };
                foreach (var pair in request.Value)
                {
                    try
                    {
                        if (OmsiImageReader.TryDecode(source.ReadAllBytes(pair.Value), out var mask))
                            decoded.Masks.Add(new KeyValuePair<int, OmsiImage>(pair.Key, mask));
                    }
                    catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                                      exception is UnauthorizedAccessException)
                    {
                        lock (problems)
                            problems.Add(pair.Value + ": " + exception.Message);
                    }
                }
                result.Add(decoded);
            }
            return result;
        }

        /// <summary>Puts the decoded masks of a tile on the map as one layer each, with the tile's terrain holes.</summary>
        private void AddMaskLayers(DecodedMasks decoded)
        {
            if (!tileStates.TryGetValue(decoded.Key, out var state) || groundRoot == null)
                return;
            state.MasksLoading = false;
            state.MasksLoaded = true;
            tileMaterials.TryGetValue(state.Key, out var renderers);
            holeTextures.TryGetValue(state.Key, out var holeTexture);
            foreach (var pair in decoded.Masks)
            {
                if (!layerMaterials.TryGetValue(pair.Key, out var layerMaterial))
                    continue;
                var material = new Material(layerMaterial);
                var texture = UploadMask(pair.Value);
                material.SetTexture("_Mask", texture);
                if (holeTexture != null)
                    SetHoles(material, holeTexture);
                state.MaskResources.Add(material);
                state.MaskResources.Add(texture);
                var meshRenderer = AddQuad("Tile " + state.X + ", " + state.Y + " layer " + pair.Key, state.Position, material, state.Mesh);
                state.MaskLayers.Add(meshRenderer.gameObject);
                renderers?.Add(meshRenderer);
            }
        }

        private void RemoveMaskLayers(TileState state)
        {
            if (tileMaterials.TryGetValue(state.Key, out var renderers))
                renderers.RemoveAll(meshRenderer => meshRenderer == null || state.MaskLayers.Contains(meshRenderer.gameObject));
            foreach (var layer in state.MaskLayers)
                Destroy(layer);
            foreach (var resource in state.MaskResources)
                Destroy(resource);
            state.MaskLayers.Clear();
            state.MaskResources.Clear();
            state.MasksLoaded = false;
        }

        /// <summary>A mask as a texture with mipmaps that its tile owns, so it can be dropped with the tile's masks.</summary>
        private Texture2D UploadMask(OmsiImage image)
        {
            var format = image.Channels == 1 ? TextureFormat.R8 : TextureFormat.RGBA32;
            var texture = new Texture2D(image.Width, image.Height, format, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var rowBytes = image.Width * image.Channels;
            var flipped = new byte[image.Pixels.Length];
            for (var row = 0; row < image.Height; row++)
                Buffer.BlockCopy(image.Pixels, row * rowBytes, flipped, (image.Height - 1 - row) * rowBytes, rowBytes);
            texture.SetPixelData(flipped, 0);
            texture.Apply(true, true);
            return texture;
        }

        private static GroundData Read(List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord[]>> tiles, List<OmsiHolePlacement> placements,
            IOmsiFileSource source, string globalConfigRelativePath, string installationRoot, double tileSize)
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
                // Masks are only listed here; they are read when their tile comes into view.
                foreach (var layer in data.Layers)
                {
                    if (!layer.Definition.HasMask)
                        continue;
                    var maskPath = layer.Definition.MaskPath(tile.FileName);
                    if (source.FileExists(maskPath))
                        tileData.MaskPaths.Add(new KeyValuePair<int, string>(layer.Definition.Index, maskPath));
                }
                AddSplines(tileData, pair.Value, profiles, installationRoot, data);
                data.Tiles.Add(tileData);
            }
            AddTerrainHoles(data, tiles, placements, profiles, installationRoot, tileSize);
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
                // Wires and fences show nothing from above; the map view draws them as lines, the 3D view as strips.
                if (profile.LineKind != OmsiSplineLineKind.None)
                {
                    data.LineKinds[spline.AssetPath] = profile.LineKind;
                    data.LineSplineCount++;
                }
                var drawn = false;
                // Upright strips, such as walls of cuttings, kerb faces and fences, matter in the 3D view.
                foreach (var surface in OmsiSplineSurface.Build(spline, profile, true))
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

        /// <summary>Draws the terrain holes of objects and splines into masks of the tiles they reach.</summary>
        private static void AddTerrainHoles(GroundData data, List<KeyValuePair<OmsiTileSummary, OmsiSplineRecord[]>> tiles,
            List<OmsiHolePlacement> placements, OmsiSplineProfileCatalog profiles, string installationRoot, double tileSize)
        {
            var available = new HashSet<long>();
            foreach (var tile in data.Tiles)
                available.Add(OmsiTerrainHoleCatalog.TileKey(tile.X, tile.Y));
            var splines = new List<KeyValuePair<long, OmsiSplineRecord>>();
            foreach (var pair in tiles)
                foreach (var spline in pair.Value)
                    splines.Add(new KeyValuePair<long, OmsiSplineRecord>(OmsiTerrainHoleCatalog.TileKey(pair.Key.X, pair.Key.Y), spline));
            var holes = new OmsiTerrainHoleCatalog(installationRoot);
            var masks = holes.Build(placements, splines, profiles, available, tileSize);
            foreach (var tile in data.Tiles)
                if (masks.TryGetValue(OmsiTerrainHoleCatalog.TileKey(tile.X, tile.Y), out var mask) && mask.HolePixelCount > 0)
                {
                    tile.Holes = mask;
                    data.HoleTileCount++;
                }
            data.Problems.AddRange(holes.Problems);
            data.Holes = new HoleSources
            {
                Catalog = holes, Profiles = profiles, Splines = splines, Tiles = available, Signature = HoleSignature(holes, placements)
            };
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
                masks += tile.MaskPaths.Count;
            var report = "Ground textures ready: " + data.Layers.Count + " texture(s), " + masks + " tile mask(s) read as their tiles come into view, " +
                         data.DrawnSplineCount + " of " + data.SplineCount + " splines with surfaces" +
                         (data.LineSplineCount > 0 ? ", " + data.LineSplineCount + " overhead lines and fences" : string.Empty) +
                         (data.HoleTileCount > 0 ? ", terrain holes in " + data.HoleTileCount + " tile(s)" : string.Empty);
            if (data.Problems.Count > 0)
                report += "; " + data.Problems.Count + " problem(s), first: " + data.Problems[0];
            if (data.Problems.Count > 0)
                Debug.LogWarning(report + "\n" + string.Join("\n", data.Problems));
            if (waterSummary != null)
                RefreshWater(waterSummary);
            Loaded?.Invoke(report + ".");
            RunQueuedRefreshes();
        }

        private void BuildTile(TileData tile)
        {
            var position = new Vector3((float)(tile.X * tileSize - originX), 0f, (float)(tile.Y * tileSize - originY));
            var mesh = tileMesh;
            if (tile.Terrain != null)
            {
                terrains[TileKey(tile.X, tile.Y)] = tile.Terrain;
                TerrainVersion++;
                mesh = CreateTerrainMesh(tile.Terrain, (float)tileSize);
                owned.Add(mesh);
            }
            var key = OmsiTerrainHoleCatalog.TileKey(tile.X, tile.Y);
            var renderers = new List<MeshRenderer>();
            tileMaterials[key] = renderers;
            if (layerMaterials.TryGetValue(0, out var baseMaterial))
                renderers.Add(AddQuad("Tile " + tile.X + ", " + tile.Y, position, baseMaterial, mesh));
            tileStates[key] = new TileState { Key = key, X = tile.X, Y = tile.Y, Position = position, Mesh = mesh, MaskPaths = tile.MaskPaths };
            AddImagerySurface(tileStates[key]);
            if (tile.Holes != null)
                ApplyHoles(key, renderers, tile.Holes);
            foreach (var batch in tile.Splines)
                AddSplineBatch("Tile " + tile.X + ", " + tile.Y + " splines", position, batch, pending.SplineTextures, key);
        }

        private static void SetHoles(Material material, Texture2D holes)
        {
            material.SetTexture("_HoleMask", holes);
            material.SetFloat("_UseHoles", 1f);
        }

        /// <summary>The hole mask as a texture; its rows already run from the south edge, as Unity expects.</summary>
        private Texture2D UploadHoles(OmsiTerrainHoleMask mask)
        {
            var texture = new Texture2D(OmsiTerrainHoleMask.Resolution, OmsiTerrainHoleMask.Resolution, TextureFormat.R8, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Terrain holes " + mask.TileX + ", " + mask.TileY
            };
            texture.SetPixelData(mask.Pixels, 0);
            texture.Apply(false, true);
            owned.Add(texture);
            return texture;
        }

        private void AddSplineBatch(string name, Vector3 position, SplineBatch batch, Dictionary<string, SplineTextureData> textures, long tileKey)
        {
            var material = SplineMaterial(batch.TextureKey, textures);
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
            if (!tileSplineObjects.TryGetValue(tileKey, out var objects))
            {
                objects = new List<GameObject>();
                tileSplineObjects.Add(tileKey, objects);
            }
            objects.Add(item);
        }

        /// <summary>
        /// Draws the water of the map's tiles as a see-through blue surface over each tile with [water], at the heights
        /// of its corners; it shows only where the tile's ground lies below it.
        /// </summary>
        public void RefreshWater(OmsiMapSummary summary)
        {
            waterSummary = summary;
            if (waterRoot != null)
            {
                Destroy(waterRoot.gameObject);
                waterRoot = null;
            }
            foreach (var resource in waterResources)
                Destroy(resource);
            waterResources.Clear();
            if (summary == null || splineShader == null)
                return;
            var size = (float)summary.TileSizeMeters;
            foreach (var tile in summary.Tiles)
            {
                if (tile.Water == null || tile.Water.Length < 4 || !tile.IsAvailable)
                    continue;
                if (waterRoot == null)
                {
                    waterRoot = new GameObject("Map Water").transform;
                    waterRoot.SetParent(transform, false);
                }
                // The water shows only where the ground lies below it: from above the ground writes no depth, so a mask
                // from the tile's terrain points fades it out over a metre of shore.
                const int points = OmsiTerrain.Resolution;
                terrains.TryGetValue(TileKey(tile.X, tile.Y), out var terrain);
                var pixels = new Color32[points * points];
                for (var row = 0; row < points; row++)
                    for (var column = 0; column < points; column++)
                    {
                        var u = column / (float)(points - 1);
                        var v = row / (float)(points - 1);
                        var level = Mathf.Lerp(Mathf.Lerp(tile.Water[0], tile.Water[1], u), Mathf.Lerp(tile.Water[2], tile.Water[3], u), v);
                        var depth = terrain != null ? level - terrain[column, row] : 1f;
                        pixels[row * points + column] = new Color32(46, 97, 133, (byte)Mathf.RoundToInt(Mathf.Clamp01(depth + 0.5f) * 184f));
                    }
                var mask = new Texture2D(points, points, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Water " + tile.X + ", " + tile.Y
                };
                mask.SetPixels32(pixels);
                mask.Apply(false, true);
                var material = new Material(splineShader) { renderQueue = AlphaSplineQueue + 10, name = mask.name };
                material.SetTexture("_MainTex", mask);
                material.SetFloat("_SourceBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DestinationBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_DepthWrite", 0f);
                material.SetFloat("_UseAlpha", 1f);
                // Corners (0, 0), (edge, 0), (0, edge), (edge, edge), as tile.map.water keeps them; the UVs meet the
                // middles of the mask's edge texels, which lie on the terrain points.
                var near = 0.5f / points;
                var far = 1f - near;
                var mesh = new Mesh { name = mask.name };
                mesh.vertices = new[]
                {
                    new Vector3(0f, tile.Water[0], 0f), new Vector3(size, tile.Water[1], 0f), new Vector3(0f, tile.Water[2], size), new Vector3(size, tile.Water[3], size)
                };
                mesh.uv = new[] { new Vector2(near, near), new Vector2(far, near), new Vector2(near, far), new Vector2(far, far) };
                mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                waterResources.Add(mask);
                waterResources.Add(material);
                waterResources.Add(mesh);
                var surface = new GameObject(mesh.name) { layer = GroundLayer };
                surface.transform.SetParent(waterRoot, false);
                surface.transform.localPosition = new Vector3((float)(tile.X * summary.TileSizeMeters - originX), 0f, (float)(tile.Y * summary.TileSizeMeters - originY));
                surface.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = surface.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        /// <summary>
        /// Lays a picture from the air over a tile's ground, following the terrain; null takes it away. The renderer takes
        /// the texture over and destroys it when it is replaced or cleared.
        /// </summary>
        public void SetImagery(int tileX, int tileY, Texture2D picture)
        {
            var key = TileKey(tileX, tileY);
            RemoveImagerySurface(key);
            DropSharp(key);
            if (imageryTextures.TryGetValue(key, out var old) && old != picture)
                Destroy(old);
            imageryTextures.Remove(key);
            if (picture == null)
                return;
            imageryTextures[key] = picture;
            if (tileStates.TryGetValue(key, out var state))
                AddImagerySurface(state);
        }

        /// <summary>Takes every tile's picture from the air away, as when another map opens.</summary>
        public void ClearImagery()
        {
            foreach (var key in new List<long>(imagerySurfaces.Keys))
                RemoveImagerySurface(key);
            foreach (var texture in imageryTextures.Values)
                Destroy(texture);
            imageryTextures.Clear();
            foreach (var key in new List<long>(sharpTextures.Keys))
                DropSharp(key);
        }

        /// <summary>
        /// Shows a tile's full picture in place of its smaller copy, while it is near the view; null goes back to the copy.
        /// The renderer takes the texture over.
        /// </summary>
        public void SetSharpImagery(int tileX, int tileY, Texture2D picture)
        {
            var key = TileKey(tileX, tileY);
            DropSharp(key);
            if (picture == null || !imageryTextures.ContainsKey(key))
            {
                if (picture != null)
                    Destroy(picture);
                ShowImageryTexture(key);
                return;
            }
            sharpTextures[key] = picture;
            ShowImageryTexture(key);
        }

        /// <summary>Whether a tile shows its full picture now.</summary>
        public bool HasSharpImagery(int tileX, int tileY)
        {
            return sharpTextures.ContainsKey(TileKey(tileX, tileY));
        }

        /// <summary>The tiles that show their full picture now, as x and y.</summary>
        public List<KeyValuePair<int, int>> SharpTiles()
        {
            var tiles = new List<KeyValuePair<int, int>>();
            foreach (var key in sharpTextures.Keys)
                tiles.Add(new KeyValuePair<int, int>((int)(key >> 32), (int)(key & 0xffffffffL)));
            return tiles;
        }

        private void DropSharp(long key)
        {
            if (!sharpTextures.TryGetValue(key, out var sharp))
                return;
            sharpTextures.Remove(key);
            Destroy(sharp);
            ShowImageryTexture(key);
        }

        private void ShowImageryTexture(long key)
        {
            if (!imagerySurfaces.TryGetValue(key, out var surface) || surface == null)
                return;
            surface.sharedMaterial.SetTexture("_MainTex", sharpTextures.TryGetValue(key, out var sharp) ? sharp : imageryTextures.TryGetValue(key, out var picture) ? picture : null);
        }

        /// <summary>How opaque the pictures from the air lie on the ground, 0 to 1.</summary>
        public float ImageryOpacity
        {
            get => imageryOpacity;
            set
            {
                imageryOpacity = Mathf.Clamp01(value);
                foreach (var pair in imagerySurfaces)
                    ApplyTileOpacity(pair.Key, pair.Value);
            }
        }

        public bool ImageryVisible
        {
            get => imageryVisible;
            set
            {
                imageryVisible = value;
                if (imageryRoot != null)
                    imageryRoot.gameObject.SetActive(value);
            }
        }

        /// <summary>A tile's picture less opaque than the others, from 0 (hidden) to 1 (as the others).</summary>
        public void SetImageryTileOpacity(int tileX, int tileY, float factor)
        {
            var key = TileKey(tileX, tileY);
            factor = Mathf.Clamp01(factor);
            if (factor >= 1f)
                imageryTileOpacity.Remove(key);
            else
                imageryTileOpacity[key] = factor;
            if (imagerySurfaces.TryGetValue(key, out var surface))
                ApplyTileOpacity(key, surface);
        }

        /// <summary>Every tile's picture as opaque as the others again, as when another map opens.</summary>
        public void ClearImageryTileOpacity()
        {
            imageryTileOpacity.Clear();
            foreach (var pair in imagerySurfaces)
                ApplyTileOpacity(pair.Key, pair.Value);
        }

        private void ApplyTileOpacity(long key, MeshRenderer surface)
        {
            if (surface == null)
                return;
            var factor = imageryTileOpacity.TryGetValue(key, out var own) ? own : 1f;
            surface.sharedMaterial.SetFloat("_Opacity", imageryOpacity * factor);
            surface.enabled = factor > 0f;
        }

        /// <summary>How many tiles have a picture from the air.</summary>
        public int ImageryTileCount => imageryTextures.Count;

        private void AddImagerySurface(TileState state)
        {
            if (splineShader == null || state.Mesh == null || !imageryTextures.TryGetValue(state.Key, out var picture))
                return;
            if (imageryRoot == null)
            {
                imageryRoot = new GameObject("Map Imagery").transform;
                imageryRoot.SetParent(transform, false);
                imageryRoot.gameObject.SetActive(imageryVisible);
            }
            var overlay = new Material(splineShader) { renderQueue = ImageryQueue, name = "Imagery " + state.X + ", " + state.Y };
            overlay.SetTexture("_MainTex", sharpTextures.TryGetValue(state.Key, out var sharp) ? sharp : picture);
            overlay.SetFloat("_SourceBlend", (float)BlendMode.SrcAlpha);
            overlay.SetFloat("_DestinationBlend", (float)BlendMode.OneMinusSrcAlpha);
            overlay.SetFloat("_DepthWrite", 0f);
            overlay.SetFloat("_UseAlpha", 1f);
            overlay.SetFloat("_Opacity", imageryOpacity);
            var surface = new GameObject(overlay.name) { layer = GroundLayer };
            surface.transform.SetParent(imageryRoot, false);
            surface.transform.localPosition = state.Position;
            surface.AddComponent<MeshFilter>().sharedMesh = state.Mesh;
            var meshRenderer = surface.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = overlay;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            imagerySurfaces[state.Key] = meshRenderer;
            ApplyTileOpacity(state.Key, meshRenderer);
        }

        private void RemoveImagerySurface(long key)
        {
            if (!imagerySurfaces.TryGetValue(key, out var surface))
                return;
            imagerySurfaces.Remove(key);
            if (surface == null)
                return;
            Destroy(surface.sharedMaterial);
            Destroy(surface.gameObject);
        }

        /// <summary>One material per spline texture, shared by all tiles.</summary>
        private Material SplineMaterial(string key, Dictionary<string, SplineTextureData> textures)
        {
            if (splineMaterials.TryGetValue(key, out var material))
                return material;
            if (splineShader == null || textures == null || !textures.TryGetValue(key, out var texture))
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

        private MeshRenderer AddQuad(string name, Vector3 position, Material material, Mesh mesh)
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
            return meshRenderer;
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
