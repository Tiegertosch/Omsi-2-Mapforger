using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.World
{
    /// <summary>
    /// Reading the objects of a map: once the ground's files are read, several workers read the terrain of its tiles and
    /// then the kinds it uses, always the kind standing nearest to the view next, and hand each kind over as soon as it is
    /// read; uploading waits until the ground is built. So the ground shows first, then the objects in view while the
    /// rest is still read. From a hard disk most of the time goes to finding the files; with several reads
    /// waiting the disk takes them in a shorter order (Spandau after a restart: 13.5 instead of 20 s for its object
    /// files with eight workers, 15.5 s with four). Read while the ground was read, they slowed it down: their garbage
    /// makes Mono's collector stop every thread; after the ground, eight load as smoothly as four.
    /// </summary>
    public sealed partial class MapObjectRenderer
    {
        private const int ReadWorkers = 8;

        /// <summary>Placements are updated at most this often while kinds arrive.</summary>
        private const float StreamPlacementSeconds = 0.5f;

        /// <summary>The objects of a map being read; workers and the main thread share it.</summary>
        private sealed class KindStream
        {
            public string InstallationRoot;
            public IOmsiFileSource Source;
            public List<KeyValuePair<long, string>> Tiles;
            public int NextTile = -1;
            public int WorkersOnTerrain = ReadWorkers;
            public readonly ConcurrentDictionary<long, OmsiTerrain> Terrains = new ConcurrentDictionary<long, OmsiTerrain>();
            public bool TerrainsTaken;

            /// <summary>Kinds not yet taken by a worker, with the middles of the tiles they stand in; guarded by itself.</summary>
            public List<KeyValuePair<string, Vector2[]>> Waiting;

            /// <summary>The point the camera looks at, in drawing coordinates; written by the main thread.</summary>
            public float ViewX;
            public float ViewY;

            public readonly ConcurrentQueue<KindData> Finished = new ConcurrentQueue<KindData>();
            public readonly ConcurrentDictionary<string, Lazy<TextureData>> Textures =
                new ConcurrentDictionary<string, Lazy<TextureData>>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Problems = new List<string>();
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            public Task Workers;
            public int Kinds;
        }

        private KindStream stream;
        private readonly List<KindData> streamUploads = new List<KindData>();
        private float nextStreamPlacement;

        /// <summary>How long the last update of the placements took.</summary>
        private float lastPlacementSeconds;

        /// <summary>Starts the workers on the terrain of <paramref name="tiles"/> and the kinds of the map, each with the tiles it stands in.</summary>
        private void StartStream(List<KeyValuePair<long, string>> tiles, Dictionary<string, List<OmsiTileSummary>> kindTiles, IOmsiFileSource source,
            string root)
        {
            var waiting = new List<KeyValuePair<string, Vector2[]>>(kindTiles.Count);
            foreach (var pair in kindTiles)
            {
                var middles = new Vector2[pair.Value.Count];
                for (var index = 0; index < middles.Length; index++)
                {
                    var tile = pair.Value[index];
                    middles[index] = new Vector2((float)((tile.X + 0.5) * tileSize - originX), (float)((tile.Y + 0.5) * tileSize - originY));
                }
                waiting.Add(new KeyValuePair<string, Vector2[]>(pair.Key, middles));
            }
            stream = new KindStream { InstallationRoot = root, Source = source, Tiles = tiles, Waiting = waiting };
            streamUploads.Clear();
            nextStreamPlacement = 0f;
        }

        /// <summary>Starts the workers; their files wait on the disk until the ground's are read.</summary>
        private static void StartWorkers(KindStream reading)
        {
            var workers = new Task[ReadWorkers];
            for (var index = 0; index < workers.Length; index++)
                workers[index] = Task.Run(() => ReadStream(reading));
            reading.Workers = Task.WhenAll(workers);
        }

        /// <summary>One worker: terrain of tiles while some are left, then kinds, nearest to the view first.</summary>
        private static void ReadStream(KindStream reading)
        {
            int at;
            while (!reading.Cancel.IsCancellationRequested && (at = Interlocked.Increment(ref reading.NextTile)) < reading.Tiles.Count)
            {
                var tile = reading.Tiles[at];
                var path = OmsiRelativePath.Normalize(tile.Value) + ".terrain";
                try
                {
                    if (reading.Source.FileExists(path))
                        reading.Terrains[tile.Key] = OmsiTerrain.Parse(reading.Source.ReadAllBytes(path));
                }
                catch (InvalidDataException exception)
                {
                    lock (reading.Problems)
                        reading.Problems.Add(path + ": " + exception.Message);
                }
            }
            Interlocked.Decrement(ref reading.WorkersOnTerrain);

            var catalog = new OmsiObjectModelCatalog(reading.InstallationRoot);
            Func<string, TextureData> texture = relativePath => reading.Textures.GetOrAdd(relativePath, key => new Lazy<TextureData>(() =>
            {
                var data = ReadTextureFile(reading.InstallationRoot, key, out var problem);
                if (problem != null)
                    lock (reading.Problems)
                        reading.Problems.Add(problem);
                return data;
            })).Value;
            string assetPath;
            while (!reading.Cancel.IsCancellationRequested && (assetPath = NearestWaiting(reading)) != null)
            {
                var kind = ReadKind(catalog, assetPath, texture);
                if (kind != null)
                    reading.Finished.Enqueue(kind);
            }
            lock (reading.Problems)
                reading.Problems.AddRange(catalog.Problems);
        }

        /// <summary>Takes the waiting kind with a tile nearest to the view, or null when none is left.</summary>
        private static string NearestWaiting(KindStream reading)
        {
            lock (reading.Waiting)
            {
                if (reading.Waiting.Count == 0)
                    return null;
                var view = new Vector2(reading.ViewX, reading.ViewY);
                var nearest = 0;
                var nearestDistance = float.MaxValue;
                for (var index = 0; index < reading.Waiting.Count; index++)
                    foreach (var middle in reading.Waiting[index].Value)
                    {
                        var distance = (middle - view).sqrMagnitude;
                        if (distance < nearestDistance)
                        {
                            nearest = index;
                            nearestDistance = distance;
                        }
                    }
                var assetPath = reading.Waiting[nearest].Key;
                reading.Waiting[nearest] = reading.Waiting[reading.Waiting.Count - 1];
                reading.Waiting.RemoveAt(reading.Waiting.Count - 1);
                return assetPath;
            }
        }

        /// <summary>Tells the workers where the camera looks: the ground point in the middle of the view.</summary>
        private void FollowView(KindStream reading)
        {
            if (targetCamera == null)
                return;
            var camera = targetCamera.transform;
            var position = camera.position;
            var forward = camera.forward;
            if (forward.y < -0.01f)
                position -= forward * (position.y / forward.y);
            reading.ViewX = position.x;
            reading.ViewY = position.z;
        }

        /// <summary>Takes over what the workers read: the terrain once it is complete, then kinds within the frame budget.</summary>
        private void ContinueStream()
        {
            var reading = stream;
            FollowView(reading);
            if (reading.Workers == null)
            {
                if (ground != null && ground.IsReading)
                    return;
                StartWorkers(reading);
            }
            if (!reading.TerrainsTaken && Volatile.Read(ref reading.WorkersOnTerrain) == 0)
            {
                foreach (var pair in reading.Terrains)
                    terrains[pair.Key] = pair.Value;
                reading.TerrainsTaken = true;
            }
            while (reading.Finished.TryDequeue(out var kind))
                streamUploads.Add(kind);

            // The ground's tiles are built within the same budget each frame; they go first.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var uploaded = 0;
            while (uploaded < streamUploads.Count && !(ground != null && ground.IsBuilding) && watch.Elapsed.TotalMilliseconds < UploadBudgetMilliseconds)
            {
                var kind = streamUploads[uploaded++];
                BuildKind(kind);
                AddKind(kind);
                reading.Kinds++;
            }
            streamUploads.RemoveRange(0, uploaded);

            var done = reading.Workers.IsCompleted && reading.Finished.IsEmpty && streamUploads.Count == 0;
            if (addedKinds.Count > 0 && (done || Time.unscaledTime >= nextStreamPlacement))
            {
                // Early on nearly every tile waits for a kind and building them takes long; they then wait longer.
                nextStreamPlacement = Time.unscaledTime + Mathf.Max(StreamPlacementSeconds, 4f * lastPlacementSeconds);
                placementsDirty = true;
                ModelsChanged?.Invoke();
            }
            if (!done)
                return;

            stream = null;
            RenewMaterialsInMapOrder();
            placementsDirty = true;
            ModelsChanged?.Invoke();
            if (reading.Workers.IsFaulted)
            {
                Debug.LogException(reading.Workers.Exception);
                Loaded?.Invoke("Objects could not be read: " + reading.Workers.Exception?.GetBaseException().Message);
                return;
            }
            if (reading.Problems.Count > 0)
                Debug.LogWarning("Object models: " + reading.Problems.Count + " problem(s)\n" + string.Join("\n", reading.Problems));
            Loaded?.Invoke("Object models ready: " + reading.Kinds + " kinds, " + textures.Count + " textures" +
                           (reading.Problems.Count > 0 ? "; " + reading.Problems.Count + " problem(s), first: " + reading.Problems[0] : string.Empty) + ".");
        }

        /// <summary>A texture the workers read, for uploading; null when it is not theirs or not read yet.</summary>
        private TextureData StreamTexture(string relativePath)
        {
            return stream != null && stream.Textures.TryGetValue(relativePath, out var texture) && texture.IsValueCreated ? texture.Value : null;
        }

        /// <summary>
        /// Objects at the same distance from the camera are drawn in the order their materials were made, and where faces
        /// lie on each other the later one shows. The workers finish kinds in no fixed order, so once all are read their
        /// materials are made again in map order, and the map looks alike however its files were read.
        /// </summary>
        private void RenewMaterialsInMapOrder()
        {
            var inOrder = true;
            for (var index = 1; index < drawOrder.Count && inOrder; index++)
                inOrder = drawOrder[index - 1].MadeAs < drawOrder[index].MadeAs;
            if (inOrder)
                return;
            var renewed = new Dictionary<Material, Material>();
            Material Renew(Material material)
            {
                if (material == null)
                    return null;
                if (!renewed.TryGetValue(material, out var copy))
                {
                    renewed[material] = copy = new Material(material) { renderQueue = material.renderQueue, enableInstancing = material.enableInstancing };
                    owned.Add(copy);
                }
                return copy;
            }
            foreach (var kind in drawOrder)
            {
                if (kind.Materials != null)
                    for (var index = 0; index < kind.Materials.Length; index++)
                        kind.Materials[index] = Renew(kind.Materials[index]);
                if (kind.BlendedMaterials != null)
                    for (var index = 0; index < kind.BlendedMaterials.Length; index++)
                        kind.BlendedMaterials[index] = Renew(kind.BlendedMaterials[index]);
                if (kind.CrossMaterial != null)
                {
                    kind.CrossMaterial = Renew(kind.CrossMaterial);
                    kind.CrossMaterials = new[] { kind.CrossMaterial };
                }
                kind.MadeAs = ++kindsMade;
            }
            owned.RemoveAll(item => item is Material material && renewed.ContainsKey(material));
            foreach (var old in renewed.Keys)
                Destroy(old);
        }

        private void StopStream()
        {
            stream?.Cancel.Cancel();
            stream = null;
            streamUploads.Clear();
        }
    }
}
