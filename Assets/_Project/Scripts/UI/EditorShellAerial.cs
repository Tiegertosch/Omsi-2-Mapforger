using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Pictures from the air and terrain heights from the world, as openMapper loads them (ADR 041): the map placed on the
    /// Earth, imagery fetched for its tiles in the background and laid over the ground, heights laid on the terrain as one
    /// step. Pictures and settings stay in the project under Mapforge and are never exported.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The edge of a tile's picture at most: 0.3 m a pixel on a 300 m tile, as much as the ground can show.</summary>
        private const int ImageryPictureSize = 1024;

        private readonly OmsiTileFetcher tileFetcher = new OmsiTileFetcher(OmsiTileFetcher.DefaultCacheRoot);
        private readonly ConcurrentQueue<Action> aerialMainThread = new ConcurrentQueue<Action>();
        private readonly Queue<OmsiTileSummary> storedImagery = new Queue<OmsiTileSummary>();
        private readonly Dictionary<long, string> imagerySources = new Dictionary<long, string>();
        private SatelliteWindow satelliteWindow;
        private Label imageryAttribution;
        private OmsiGeoSettings geoSettings;
        private CancellationTokenSource aerialCancel;
        private Task aerialTask;
        private int aerialGeneration;
        private bool? satelliteTileChosen;
        private string aerialMapKey;

        // Tiles added while "New tiles get pictures by themselves" is ticked, by position, waiting for the load running now.
        private readonly List<KeyValuePair<int, int>> autoTiles = new List<KeyValuePair<int, int>>();

        private void InitializeSatellite(VisualElement root, VisualElement viewMenu)
        {
            satelliteWindow = new SatelliteWindow();
            satelliteWindow.style.display = DisplayStyle.None;
            workspaceElement?.Add(satelliteWindow);
            satelliteWindow.Closed += () => HidePopup(satelliteWindow);
            satelliteWindow.SearchRequested += SearchPlace;
            satelliteWindow.PlaceChosen += place =>
            {
                var at = OmsiGeoreference.MercatorOf(place.Latitude, place.Longitude);
                satelliteWindow.WorldMap.Marker = at;
                satelliteWindow.WorldMap.LookAt(at, 16.0);
            };
            satelliteWindow.PlaceRequested += PlaceMapAt;
            satelliteWindow.RotationChanged += degrees =>
            {
                if (geoSettings?.Anchor == null)
                    return;
                geoSettings.Anchor.RotationDegrees = degrees;
                SaveGeo();
                ShowGeo();
            };
            satelliteWindow.SourceChanged += name =>
            {
                if (geoSettings == null)
                    return;
                geoSettings.Source = name;
                SaveGeo();
            };
            satelliteWindow.LoadRequested += (selectedOnly, pictures, heights) => StartAerialLoad(selectedOnly, pictures, heights);
            satelliteWindow.CancelRequested += () =>
            {
                autoTiles.Clear();
                aerialCancel?.Cancel();
            };
            satelliteWindow.TicksChanged += (withTerrain, autoNewTiles) =>
            {
                if (geoSettings == null)
                    return;
                geoSettings.WithTerrain = withTerrain;
                geoSettings.AutoNewTiles = autoNewTiles;
                SaveGeo();
            };
            satelliteWindow.HeightOptionsChanged += () =>
            {
                if (geoSettings == null)
                    return;
                geoSettings.Heights.Relative = satelliteWindow.Relative;
                geoSettings.Heights.Offset = satelliteWindow.Offset ?? geoSettings.Heights.Offset;
                geoSettings.Heights.Smoothing = Math.Max(0.0, satelliteWindow.Smoothing ?? geoSettings.Heights.Smoothing);
                SaveGeo();
                ShowGeo();
            };
            satelliteWindow.MeasureAgainRequested += () =>
            {
                if (geoSettings == null)
                    return;
                geoSettings.Heights.Base = null;
                SaveGeo();
                ShowGeo();
            };
            satelliteWindow.OverlayChanged += (show, opacity) =>
            {
                if (groundRenderer != null)
                {
                    groundRenderer.ImageryVisible = show;
                    groundRenderer.ImageryOpacity = opacity;
                }
                UpdateImageryAttribution();
                if (geoSettings == null)
                    return;
                geoSettings.ShowImagery = show;
                geoSettings.ImageryOpacity = opacity;
                SaveGeo();
            };

            imageryAttribution = new Label { pickingMode = PickingMode.Ignore };
            imageryAttribution.AddToClassList("imagery-attribution");
            imageryAttribution.style.display = DisplayStyle.None;
            workspaceElement?.Add(imageryAttribution);

            var menuItem = root.Q<Button>("satellite-menu-item");
            if (menuItem != null)
                menuItem.clicked += () =>
                {
                    HidePopup(viewMenu);
                    OpenSatelliteWindow();
                };
        }

        /// <summary>Shows the window with the project's place and settings; a map without a project cannot keep pictures.</summary>
        private void OpenSatelliteWindow()
        {
            if (satelliteWindow == null || currentSummary == null)
            {
                SetStatus("Open a map first.");
                return;
            }
            if (session?.Workspace == null)
            {
                SetStatus("Save the map as a project first (File > Save map project as...): the pictures and the map's place stay in the project.");
                return;
            }
            geoSettings = geoSettings ?? session.ReadGeoSettings();
            satelliteWindow.style.display = DisplayStyle.Flex;
            satelliteWindow.WorldMap.SetSource(tileFetcher, OmsiImagerySource.Imagery[0]);
            satelliteWindow.ShowAttribution(OmsiImagerySource.Imagery[0].Attribution + "  ·  Search © OpenStreetMap contributors");
            ShowGeo();
            var geo = Georeference();
            var middle = MiddleTile();
            if (geo != null && middle != null)
            {
                var size = currentSummary.TileSizeMeters;
                satelliteWindow.WorldMap.LookAt(geo.Mercator((middle.X + 0.5) * size, (middle.Y + 0.5) * size), 14.0);
            }
            if (aerialTask == null)
                satelliteWindow.ShowProgress(groundRenderer != null && groundRenderer.ImageryTileCount > 0
                    ? groundRenderer.ImageryTileCount + " tile(s) have pictures from the air."
                    : string.Empty, false);
        }

        /// <summary>Shows the settings and where the map lies, with its tiles outlined on the map of the world.</summary>
        private void ShowGeo()
        {
            if (satelliteWindow == null || geoSettings == null || currentSummary == null)
                return;
            var world = currentSummary.UsesWorldCoordinates;
            satelliteWindow.Show(geoSettings, world, geoSettings.Source);
            satelliteWindow.ShowAnchor(world ? null : geoSettings.Anchor, world);
            satelliteTileChosen = null;
            var map = satelliteWindow.WorldMap;
            map.Outlines.Clear();
            map.AnchorOutline = -1;
            var geo = Georeference();
            if (geo != null)
            {
                var size = currentSummary.TileSizeMeters;
                var middle = MiddleTile();
                foreach (var tile in currentSummary.Tiles)
                {
                    if (!tile.IsAvailable)
                        continue;
                    if (tile == middle)
                        map.AnchorOutline = map.Outlines.Count;
                    map.Outlines.Add(new[]
                    {
                        geo.Mercator(tile.X * size, tile.Y * size), geo.Mercator((tile.X + 1) * size, tile.Y * size),
                        geo.Mercator((tile.X + 1) * size, (tile.Y + 1) * size), geo.Mercator(tile.X * size, (tile.Y + 1) * size)
                    });
                }
            }
            map.RefreshOutlines();
        }

        private OmsiGeoreference Georeference()
        {
            return currentSummary == null ? null : OmsiGeoreference.Of(currentSummary, geoSettings?.Anchor);
        }

        /// <summary>The tile nearest the middle of the map's tiles: the anchor, and the tile relative heights are measured at.</summary>
        private OmsiTileSummary MiddleTile()
        {
            if (currentSummary == null)
                return null;
            double sumX = 0, sumY = 0;
            var count = 0;
            foreach (var tile in currentSummary.Tiles)
                if (tile.IsAvailable)
                {
                    sumX += tile.X;
                    sumY += tile.Y;
                    count++;
                }
            if (count == 0)
                return null;
            OmsiTileSummary best = null;
            var bestDistance = double.MaxValue;
            foreach (var tile in currentSummary.Tiles)
            {
                if (!tile.IsAvailable)
                    continue;
                var distance = Math.Pow(tile.X - sumX / count, 2) + Math.Pow(tile.Y - sumY / count, 2);
                if (distance < bestDistance)
                {
                    best = tile;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>Lays the middle of the anchor tile on a Web Mercator position, keeping the turn of the map's north.</summary>
        private void PlaceMapAt(OmsiMapPoint mercator)
        {
            if (geoSettings == null || currentSummary == null || currentSummary.UsesWorldCoordinates)
                return;
            var middle = MiddleTile();
            if (middle == null)
                return;
            var position = OmsiGeoreference.LatLonOfMercator(mercator.X, mercator.Y);
            geoSettings.Anchor = new OmsiGeoAnchor
            {
                TileX = middle.X, TileY = middle.Y, Latitude = position.Latitude, Longitude = position.Longitude,
                RotationDegrees = geoSettings.Anchor?.RotationDegrees ?? 0.0
            };
            SaveGeo();
            ShowGeo();
            SetStatus("The map lies at " + position.Latitude.ToString("0.000000", CultureInfo.InvariantCulture) + "°, " +
                      position.Longitude.ToString("0.000000", CultureInfo.InvariantCulture) + "°." +
                      (groundRenderer != null && groundRenderer.ImageryTileCount > 0 ? " Pictures loaded before show the old place: load them again." : string.Empty));
        }

        private void SaveGeo()
        {
            if (geoSettings == null || session?.Workspace == null)
                return;
            try
            {
                session.SaveGeoSettings(geoSettings);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus("The map's place could not be saved: " + exception.Message);
            }
        }

        private void SearchPlace(string text)
        {
            satelliteWindow?.ShowPlaces(new List<OmsiPlace>(), null);
            var generation = aerialGeneration;
            Task.Run(() => tileFetcher.Search(text, CancellationToken.None)).ContinueWith(task => aerialMainThread.Enqueue(() =>
            {
                if (generation != aerialGeneration || satelliteWindow == null)
                    return;
                if (task.IsFaulted)
                {
                    satelliteWindow.ShowPlaces(null, "The search did not answer: " + task.Exception?.GetBaseException().Message);
                    return;
                }
                if (task.Result.Count == 0)
                {
                    satelliteWindow.ShowPlaces(null, "Nothing found by that name.");
                    return;
                }
                satelliteWindow.ShowPlaces(task.Result, null);
                var first = OmsiGeoreference.MercatorOf(task.Result[0].Latitude, task.Result[0].Longitude);
                satelliteWindow.WorldMap.Marker = first;
                satelliteWindow.WorldMap.LookAt(first, 15.0);
            }));
        }

        /// <summary>
        /// Fetches pictures and heights in the background: for the selected tile or for all, the middle first. Pictures
        /// arrive one tile at a time; heights are laid on the map together at the end as one step.
        /// </summary>
        private void StartAerialLoad(bool selectedOnly, bool pictures, bool heights, List<OmsiTileSummary> only = null)
        {
            if (aerialTask != null || currentSummary == null || session?.Workspace == null || geoSettings == null)
                return;
            var geo = Georeference();
            if (geo == null)
            {
                satelliteWindow?.ShowProgress("Place the map on the Earth first.", false, true);
                return;
            }
            if (heights && !CanEdit)
            {
                satelliteWindow?.ShowProgress(ReadOnlyMessage("load terrain heights"), false, true);
                return;
            }
            var middle = MiddleTile();
            var tiles = new List<OmsiTileSummary>();
            if (only != null)
                tiles.AddRange(only);
            else if (selectedOnly)
            {
                if (selectedTile == null || !selectedTile.IsAvailable)
                {
                    satelliteWindow?.ShowProgress("Select a tile in the map first.", false, true);
                    return;
                }
                tiles.Add(selectedTile);
            }
            else
            {
                foreach (var tile in currentSummary.Tiles)
                    if (tile.IsAvailable)
                        tiles.Add(tile);
                tiles.Sort((a, b) => (Math.Abs(a.X - middle.X) + Math.Abs(a.Y - middle.Y)).CompareTo(Math.Abs(b.X - middle.X) + Math.Abs(b.Y - middle.Y)));
            }

            var cancel = new CancellationTokenSource();
            var job = new OmsiAerialJob
            {
                Geo = geo, TileSize = currentSummary.TileSizeMeters, Fetcher = tileFetcher, Decode = bytes => DecodeOnMainThread(bytes, cancel.Token),
                MaxImageSize = ImageryPictureSize,
                Heights = heights
                    ? new OmsiGeoHeightOptions
                    {
                        Relative = geoSettings.Heights.Relative, Offset = geoSettings.Heights.Offset, Smoothing = geoSettings.Heights.Smoothing,
                        Base = geoSettings.Heights.Base
                    }
                    : null
            };
            job.Tiles.AddRange(tiles);
            if (pictures)
            {
                var chosen = FindImagerySource(geoSettings.Source);
                if (chosen != null)
                    job.ImagerySources.Add(chosen);
                else
                    job.ImagerySources.AddRange(OmsiImagerySource.Imagery);
            }

            var generation = ++aerialGeneration;
            aerialCancel = cancel;
            var laid = new List<KeyValuePair<OmsiTileSummary, float[]>>();
            var failed = new List<string>();
            var pictured = 0;
            var stamp = geo.Stamp;
            job.Status = text => aerialMainThread.Enqueue(() =>
            {
                if (generation != aerialGeneration)
                    return;
                satelliteWindow?.ShowProgress(text, true);
                if (job.ChosenSource != null && text.StartsWith(job.ChosenSource.Name, StringComparison.Ordinal))
                    satelliteWindow?.ShowDetail(text);
                SetStatus("Satellite: " + text);
            });
            job.ImageReady = (tile, picture, meta) => aerialMainThread.Enqueue(() =>
            {
                if (generation != aerialGeneration)
                    return;
                if (ShowPicture(tile, picture, meta, true))
                    pictured++;
            });
            job.HeightsReady = (tile, values) =>
            {
                lock (laid)
                    laid.Add(new KeyValuePair<OmsiTileSummary, float[]>(tile, values));
            };
            job.TileFailed = (tile, message) =>
            {
                lock (failed)
                    failed.Add("Tile " + tile.X + ", " + tile.Y + ": " + message);
            };
            satelliteWindow?.ShowProgress("Starting...", true);
            aerialTask = Task.Run(() => job.Run(cancel.Token), cancel.Token);
            aerialTask.ContinueWith(task => aerialMainThread.Enqueue(() =>
            {
                if (generation != aerialGeneration)
                    return;
                aerialTask = null;
                aerialCancel = null;
                FinishAerialLoad(task, job, laid, failed, pictured, tiles.Count, stamp);
                StartAutoLoad();
            }));
        }

        /// <summary>
        /// A tile was added: with "New tiles get pictures by themselves" it gets its picture, and its heights when they are
        /// ticked, as soon as no other load runs.
        /// </summary>
        private void TileAddedForImagery(int x, int y)
        {
            if (geoSettings == null || !geoSettings.AutoNewTiles || Georeference() == null)
                return;
            autoTiles.Add(new KeyValuePair<int, int>(x, y));
            StartAutoLoad();
        }

        private void StartAutoLoad()
        {
            if (aerialTask != null || autoTiles.Count == 0 || currentSummary == null || geoSettings == null)
                return;
            var tiles = new List<OmsiTileSummary>();
            foreach (var position in autoTiles)
            {
                var tile = currentSummary.Tiles.Find(candidate => candidate.X == position.Key && candidate.Y == position.Value);
                if (tile != null && tile.IsAvailable)
                    tiles.Add(tile);
            }
            autoTiles.Clear();
            if (tiles.Count > 0)
                StartAerialLoad(false, true, geoSettings.WithTerrain && CanEdit, tiles);
        }

        private void FinishAerialLoad(Task task, OmsiAerialJob job, List<KeyValuePair<OmsiTileSummary, float[]>> laid, List<string> failed, int pictured, int total, string stamp)
        {
            var cancelled = task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException;
            if (task.IsFaulted && !cancelled)
            {
                var message = task.Exception?.GetBaseException().Message;
                Debug.LogWarning("Satellite: " + message);
                satelliteWindow?.ShowProgress(message, false, true);
                SetStatus("Satellite: " + message);
                return;
            }
            var report = new List<string>();
            if (pictured > 0)
                report.Add(pictured + " of " + total + " tile(s) with pictures" + (job.ChosenSource != null ? " from " + job.ChosenSource.Name : string.Empty));
            List<KeyValuePair<OmsiTileSummary, float[]>> heights;
            lock (laid)
                heights = new List<KeyValuePair<OmsiTileSummary, float[]>>(laid);
            if (heights.Count > 0 && CanEdit)
            {
                try
                {
                    var change = session.ImportTerrainHeights(currentSummary, heights);
                    if (change != null)
                    {
                        PushHistory(new ObjectEditStep { TerrainChange = change, Description = "the terrain heights from the world" });
                        RefreshGround(change.Tiles);
                        report.Add("heights on " + change.Tiles.Count + " tile(s), Ctrl+Z takes them back");
                    }
                    if (job.Base.HasValue && geoSettings != null && geoSettings.Heights.Relative)
                    {
                        geoSettings.Heights.Base = job.Base;
                        SaveGeo();
                        ShowGeo();
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
                {
                    Debug.LogException(exception);
                    report.Add("the heights could not be laid on the map: " + exception.Message);
                }
            }
            if (failed.Count > 0)
            {
                Debug.LogWarning("Satellite problems:\n" + string.Join("\n", failed));
                report.Add(failed.Count + " tile(s) with problems, first: " + failed[0]);
            }
            var text = (cancelled ? "Cancelled: " : "Done: ") + (report.Count > 0 ? string.Join("; ", report) : "nothing loaded") + ".";
            satelliteWindow?.ShowProgress(text, false, failed.Count > 0);
            SetStatus("Satellite: " + text);
        }

        /// <summary>
        /// A picture of a tile: kept in the project as a JPEG when it is new, then laid over the ground. Its rows run from
        /// the north edge; the texture's from the south, as the ground's UVs.
        /// </summary>
        private bool ShowPicture(OmsiTileSummary tile, OmsiRgbImage picture, OmsiImageryMeta meta, bool keep)
        {
            if (groundRenderer == null || picture == null)
                return false;
            var size = picture.Width;
            var rows = new byte[picture.Rgb.Length];
            var stride = size * 3;
            for (var row = 0; row < picture.Height; row++)
                Array.Copy(picture.Rgb, row * stride, rows, (picture.Height - 1 - row) * stride, stride);
            var texture = new Texture2D(size, picture.Height, TextureFormat.RGB24, true)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Imagery " + tile.X + ", " + tile.Y
            };
            texture.SetPixelData(rows, 0);
            texture.Apply(true, false);
            if (keep)
            {
                try
                {
                    session.SaveImagery(tile, texture.EncodeToJPG(90), meta);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
                {
                    Debug.LogException(exception);
                    SetStatus("The picture of tile " + tile.X + ", " + tile.Y + " could not be kept: " + exception.Message);
                }
            }
            LayPicture(tile, texture, meta);
            return true;
        }

        private void LayPicture(OmsiTileSummary tile, Texture2D texture, OmsiImageryMeta meta)
        {
            texture.Compress(false);
            texture.Apply(false, true);
            groundRenderer.SetImagery(tile.X, tile.Y, texture);
            imagerySources[MapTileKey(tile)] = meta?.Source;
            UpdateImageryAttribution();
        }

        private static OmsiImagerySource FindImagerySource(string name)
        {
            foreach (var source in OmsiImagerySource.Imagery)
                if (source.Name == name)
                    return source;
            return null;
        }

        private static long MapTileKey(OmsiTileSummary tile)
        {
            return ((long)tile.X << 32) | (uint)tile.Y;
        }

        /// <summary>The sources of the pictures on the ground, as their terms ask, while the pictures show.</summary>
        private void UpdateImageryAttribution()
        {
            if (imageryAttribution == null)
                return;
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var name in imagerySources.Values)
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            var lines = new List<string>();
            foreach (var name in names)
            {
                lines.Add(FindImagerySource(name)?.Attribution ?? name);
            }
            var visible = lines.Count > 0 && groundRenderer != null && groundRenderer.ImageryVisible && groundRenderer.ImageryTileCount > 0;
            imageryAttribution.text = string.Join("  ·  ", lines);
            imageryAttribution.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Turns a picture's file into pixels for the background job: PNG on its own thread, JPEG on the main thread, which
        /// is the only one Unity decodes on. Null where it cannot be read.
        /// </summary>
        private OmsiRgbImage DecodeOnMainThread(byte[] bytes, CancellationToken cancel)
        {
            if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G')
            {
                try
                {
                    return OmsiPng.DecodeRgb(bytes);
                }
                catch (InvalidDataException)
                {
                    return null;
                }
            }
            OmsiRgbImage result = null;
            var done = new ManualResetEventSlim();
            aerialMainThread.Enqueue(() =>
            {
                try
                {
                    result = DecodePicture(bytes);
                }
                finally
                {
                    done.Set();
                }
            });
            // Cancelling ends the wait; the shell stops pumping only on quit, which cancels too.
            done.Wait(cancel);
            return result;
        }

        private static OmsiRgbImage DecodePicture(byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!texture.LoadImage(bytes, false))
                    return null;
                var pixels = texture.GetPixels32();
                var width = texture.width;
                var height = texture.height;
                var rgb = new byte[width * height * 3];
                for (var y = 0; y < height; y++)
                {
                    var from = (height - 1 - y) * width;
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = pixels[from + x];
                        var index = (y * width + x) * 3;
                        rgb[index] = pixel.r;
                        rgb[index + 1] = pixel.g;
                        rgb[index + 2] = pixel.b;
                    }
                }
                return new OmsiRgbImage(width, height, rgb);
            }
            finally
            {
                Destroy(texture);
            }
        }

        /// <summary>
        /// A map was opened: the pictures of the one before go, a load still running stops, and the project's own pictures
        /// are laid on the ground a few each frame. The same map read again, as after adding a tile, keeps its pictures
        /// and its load; only pictures of tiles no longer there go.
        /// </summary>
        private void LoadStoredImagery(string mapKey)
        {
            if (mapKey == aerialMapKey && currentSummary != null)
            {
                var present = new HashSet<long>();
                foreach (var tile in currentSummary.Tiles)
                    if (tile.IsAvailable)
                        present.Add(MapTileKey(tile));
                foreach (var key in new List<long>(imagerySources.Keys))
                    if (!present.Contains(key))
                    {
                        groundRenderer?.SetImagery((int)(key >> 32), (int)(key & 0xffffffffL), null);
                        imagerySources.Remove(key);
                    }
                UpdateImageryAttribution();
                if (satelliteWindow != null && IsShown(satelliteWindow))
                    OpenSatelliteWindow();
                return;
            }
            aerialMapKey = mapKey;
            autoTiles.Clear();
            aerialGeneration++;
            aerialCancel?.Cancel();
            aerialCancel = null;
            aerialTask = null;
            storedImagery.Clear();
            imagerySources.Clear();
            groundRenderer?.ClearImagery();
            geoSettings = session?.Workspace != null ? session.ReadGeoSettings() : null;
            if (groundRenderer != null)
            {
                groundRenderer.ImageryVisible = geoSettings?.ShowImagery ?? true;
                groundRenderer.ImageryOpacity = (float)(geoSettings?.ImageryOpacity ?? 1.0);
            }
            UpdateImageryAttribution();
            if (satelliteWindow != null && IsShown(satelliteWindow))
            {
                if (geoSettings == null)
                    HidePopup(satelliteWindow);
                else
                    OpenSatelliteWindow();
            }
            if (session?.Workspace == null || currentSummary == null)
                return;
            foreach (var tile in currentSummary.Tiles)
                if (tile.IsAvailable && session.Workspace.IsModified(OmsiMapSession.ImageryPath(tile)))
                    storedImagery.Enqueue(tile);
        }

        /// <summary>Runs what the background work handed to the main thread, and lays stored pictures, within a few milliseconds.</summary>
        private void PumpAerial()
        {
            var started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 0.012f && aerialMainThread.TryDequeue(out var action))
                action();
            while (Time.realtimeSinceStartup - started < 0.012f && storedImagery.Count > 0)
            {
                var tile = storedImagery.Dequeue();
                if (session == null)
                    break;
                var bytes = session.ReadImagery(tile, out var meta);
                if (bytes == null)
                    continue;
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, true)
                {
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Imagery " + tile.X + ", " + tile.Y
                };
                if (!texture.LoadImage(bytes, false))
                {
                    Destroy(texture);
                    Debug.LogWarning("The picture of tile " + tile.X + ", " + tile.Y + " could not be read.");
                    continue;
                }
                if (meta != null && geoSettings != null && meta.Stamp != Georeference()?.Stamp)
                    Debug.Log("The picture of tile " + tile.X + ", " + tile.Y + " was made for another place of the map.");
                LayPicture(tile, texture, meta);
            }
            if (satelliteWindow != null && IsShown(satelliteWindow))
            {
                var chosen = selectedTile != null && selectedTile.IsAvailable;
                if (satelliteTileChosen != chosen)
                {
                    satelliteTileChosen = chosen;
                    satelliteWindow.ShowTileChosen(chosen);
                }
            }
        }

        /// <summary>The status for the player log, as the batch checks read it.</summary>
        private void LogSatellite()
        {
            var text = statusLabel?.text ?? string.Empty;
            Debug.Log(text.StartsWith("Satellite:", StringComparison.Ordinal) ? text : "Satellite: " + text);
        }

        private void OnDestroy()
        {
            aerialCancel?.Cancel();
            satelliteWindow?.WorldMap.Shutdown();
        }

        /// <summary>
        /// -satellite opens the window; -satelliteSearch &lt;text&gt; finds a place; -satellitePlace &lt;latitude&gt; &lt;longitude&gt;
        /// [rotation] lays the map's middle there; -satelliteSource &lt;name&gt; chooses a source; -satelliteTile &lt;x&gt; &lt;y&gt; selects
        /// a tile; -satelliteLoad all|tile|heights [-satelliteHeights] loads and waits for the end; -satelliteOpacity &lt;0..1&gt;.
        /// Statuses go to the log as "Satellite:".
        /// </summary>
        private IEnumerator RunSatelliteArguments(string[] args)
        {
            string Argument(string name, int offset)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + offset < args.Length ? args[index + offset] : null;
            }
            if (Array.IndexOf(args, "-satellite") < 0)
                yield break;
            var groundWait = Time.realtimeSinceStartup;
            while (groundRenderer != null && groundRenderer.IsLoading && Time.realtimeSinceStartup - groundWait < 300f)
                yield return null;
            while (storedImagery.Count > 0)
                yield return null;
            var autoWait = Time.realtimeSinceStartup;
            while ((aerialTask != null || autoTiles.Count > 0) && Time.realtimeSinceStartup - autoWait < 600f)
                yield return null;
            OpenSatelliteWindow();
            if (!IsShown(satelliteWindow))
            {
                LogSatellite();
                yield break;
            }
            var search = Argument("-satelliteSearch", 1);
            if (search != null)
            {
                SearchPlace(search);
                var searchWait = Time.realtimeSinceStartup;
                while (satelliteWindow.WorldMap.Marker == null && Time.realtimeSinceStartup - searchWait < 30f)
                    yield return null;
            }
            if (double.TryParse(Argument("-satellitePlace", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) &&
                double.TryParse(Argument("-satellitePlace", 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                if (double.TryParse(Argument("-satellitePlace", 3), NumberStyles.Float, CultureInfo.InvariantCulture, out var rotation) && geoSettings != null)
                    geoSettings.Anchor = new OmsiGeoAnchor { RotationDegrees = rotation };
                PlaceMapAt(OmsiGeoreference.MercatorOf(latitude, longitude));
                OpenSatelliteWindow();
                LogSatellite();
            }
            var source = Argument("-satelliteSource", 1);
            if (source != null && geoSettings != null)
            {
                geoSettings.Source = source == "sharpest" ? null : source;
                SaveGeo();
                ShowGeo();
            }
            if (float.TryParse(Argument("-satelliteOpacity", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) && groundRenderer != null)
            {
                groundRenderer.ImageryOpacity = opacity;
                if (geoSettings != null)
                {
                    geoSettings.ImageryOpacity = opacity;
                    SaveGeo();
                    ShowGeo();
                }
            }
            if (int.TryParse(Argument("-satelliteTile", 1), out var chosenX) && int.TryParse(Argument("-satelliteTile", 2), out var chosenY))
                selectedTile = currentSummary.Tiles.Find(tile => tile.X == chosenX && tile.Y == chosenY) ?? selectedTile;
            var load = Argument("-satelliteLoad", 1);
            if (load != null)
            {
                StartAerialLoad(load == "tile", load != "heights", load == "heights" || Array.IndexOf(args, "-satelliteHeights") >= 0);
                var loadWait = Time.realtimeSinceStartup;
                while (aerialTask != null && Time.realtimeSinceStartup - loadWait < 600f)
                    yield return null;
                // The last pictures reach the ground through the main thread's queue.
                for (var frame = 0; frame < 10; frame++)
                    yield return null;
                LogSatellite();
            }
        }
    }
}
