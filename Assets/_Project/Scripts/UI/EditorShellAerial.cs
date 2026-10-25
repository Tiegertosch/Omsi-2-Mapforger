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
using UnityEngine.Experimental.Rendering;
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
        /// <summary>
        /// The edge of the pictures the ground shows of every tile: 0.3 m a pixel on a 300 m tile. Larger pictures keep a
        /// copy of this size, and their full picture is shown only for the tiles near the view when zoomed in.
        /// </summary>
        private const int ImageryPictureSize = 1024;

        /// <summary>Tiles shown with their full picture at most, the nearest to the middle of the view.</summary>
        private const int SharpTileLimit = 9;

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
        private OmsiImageryPreferences imageryPreferences = new OmsiImageryPreferences();
        private List<OmsiTerrainEdge> steppedEdges = new List<OmsiTerrainEdge>();
        private float sharpCheckedAt;
        private string aerialMapKey;

        // Tiles added while "New tiles get pictures by themselves" is ticked, by position, waiting for the load running now.
        private readonly List<KeyValuePair<int, int>> autoTiles = new List<KeyValuePair<int, int>>();

        private void InitializeSatellite(VisualElement root, VisualElement viewMenu)
        {
            ReadImageryPreferences();
            tileFetcher.Keys = name => imageryPreferences.Key(name);
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
            satelliteWindow.PictureSizeChanged += size =>
            {
                if (geoSettings == null)
                    return;
                geoSettings.PictureSize = size;
                SaveGeo();
            };
            satelliteWindow.EdgesFitRequested += FitSteppedEdges;
            satelliteWindow.EdgesLeft += () =>
            {
                steppedEdges.Clear();
                satelliteWindow.ShowSteppedEdges(0);
            };
            satelliteWindow.KeyChanged += (name, key) =>
            {
                if ((imageryPreferences.Key(name) ?? string.Empty) == (key ?? string.Empty).Trim())
                    return;
                if (string.IsNullOrWhiteSpace(key))
                    imageryPreferences.Keys.Remove(name);
                else
                    imageryPreferences.Keys[name] = key.Trim();
                SaveImageryPreferences();
                SetStatus(string.IsNullOrWhiteSpace(key) ? "The key for " + name + " is taken away." : "The key for " + name + " is kept on this computer.");
            };
            satelliteWindow.OwnSourceAdded += (name, url, attribution, zoom, wms) =>
            {
                OmsiImagerySource source;
                try
                {
                    source = OmsiImagerySource.Own(name, url, attribution, zoom, wms);
                }
                catch (ArgumentException exception)
                {
                    satelliteWindow.ShowOwnSourceProblem(exception.Message);
                    return;
                }
                if (imageryPreferences.All().Exists(item => item.Name == source.Name) || source.Name == OmsiImagerySource.OsmMap.Name)
                {
                    satelliteWindow.ShowOwnSourceProblem("A source of that name is there already.");
                    return;
                }
                if (!wms && !(url.Contains("{z}") && url.Contains("{x}") && url.Contains("{y}")) || wms && !url.Contains("{bbox}"))
                {
                    satelliteWindow.ShowOwnSourceProblem(wms ? "A WMS URL needs {bbox}." : "A tile URL needs {z}, {x} and {y}.");
                    return;
                }
                imageryPreferences.Sources.Add(source);
                SaveImageryPreferences();
                satelliteWindow.ShowOwnSourceProblem(null);
                ShowImagerySources();
            };
            satelliteWindow.OwnSourceRemoved += name =>
            {
                imageryPreferences.Sources.RemoveAll(source => source.Name == name);
                imageryPreferences.Keys.Remove(name);
                SaveImageryPreferences();
                ShowImagerySources();
            };
            satelliteWindow.PictureLoadRequested += (west, south, east, north) => LoadOwnPicture(west, south, east, north);
            satelliteWindow.PictureSelected += id =>
            {
                selectedPictureId = id;
                ShowOwnPictures();
            };
            satelliteWindow.PictureFitChanged += ChangePictureFit;
            satelliteWindow.PictureRemoved += RemoveOwnPicture;
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
            ShowImagerySources();
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
                MaxImageSize = 4096, PictureSize = geoSettings.PictureSize, Font = OsmFont(),
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
                    foreach (var source in imageryPreferences.All())
                        if (imageryPreferences.Usable(source))
                            job.ImagerySources.Add(source);
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
            job.ImageReady = (tile, picture, meta) =>
            {
                // Made smaller and written as JPEG here, off the main thread.
                var prepared = PreparePicture(picture);
                aerialMainThread.Enqueue(() =>
                {
                    if (generation != aerialGeneration)
                        return;
                    if (ShowPicture(tile, prepared, meta))
                        pictured++;
                });
            };
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
                    var stepped = new List<OmsiTerrainEdge>();
                    var change = session.ImportTerrainHeights(currentSummary, heights, stepped);
                    if (change != null)
                    {
                        PushHistory(new ObjectEditStep { TerrainChange = change, Description = "the terrain heights from the world" });
                        RefreshGround(change.Tiles);
                        report.Add("heights on " + change.Tiles.Count + " tile(s), Ctrl+Z takes them back");
                    }
                    steppedEdges = stepped;
                    var steppedTiles = new HashSet<long>();
                    foreach (var edge in stepped)
                        steppedTiles.Add(((long)edge.TileX << 32) | (uint)edge.TileY);
                    satelliteWindow?.ShowSteppedEdges(steppedTiles.Count);
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
            if (job.PassedOver.Count > 0)
                report.Add("passed over: " + string.Join(" ", job.PassedOver));
            if (failed.Count > 0)
            {
                Debug.LogWarning("Satellite problems:\n" + string.Join("\n", failed));
                report.Add(failed.Count + " tile(s) with problems, first: " + failed[0]);
            }
            var text = (cancelled ? "Cancelled: " : "Done: ") + (report.Count > 0 ? string.Join("; ", report) : "nothing loaded") + ".";
            satelliteWindow?.ShowProgress(text, false, failed.Count > 0);
            SetStatus("Satellite: " + text);
        }

        /// <summary>A tile's picture made ready off the main thread: its copy for the view, and both as JPEG where Unity allows it.</summary>
        private sealed class PreparedPicture
        {
            public byte[] ViewRows;
            public int ViewWidth;
            public int ViewHeight;
            public byte[] FullJpeg;
            public byte[] ViewJpeg;
            public bool Larger;

            // The full picture's rows, kept only when it has to be written as JPEG on the main thread.
            public byte[] FullRows;
            public int FullWidth;
            public int FullHeight;
        }

        /// <summary>
        /// Rows from the south, as the texture and the ground's UVs have them; the picture's rows run from the north edge.
        /// </summary>
        private static byte[] BottomUp(OmsiRgbImage picture)
        {
            var rows = new byte[picture.Rgb.Length];
            var stride = picture.Width * 3;
            for (var row = 0; row < picture.Height; row++)
                Array.Copy(picture.Rgb, row * stride, rows, (picture.Height - 1 - row) * stride, stride);
            return rows;
        }

        private static PreparedPicture PreparePicture(OmsiRgbImage picture)
        {
            if (picture == null)
                return null;
            var view = picture.Reduced(ImageryPictureSize);
            var prepared = new PreparedPicture { ViewRows = BottomUp(view), ViewWidth = view.Width, ViewHeight = view.Height, Larger = view != picture };
            var fullRows = prepared.Larger ? BottomUp(picture) : prepared.ViewRows;
            try
            {
                prepared.FullJpeg = ImageConversion.EncodeArrayToJPG(fullRows, GraphicsFormat.R8G8B8_SRGB, (uint)picture.Width, (uint)picture.Height, 0, 90);
                if (prepared.Larger)
                    prepared.ViewJpeg = ImageConversion.EncodeArrayToJPG(prepared.ViewRows, GraphicsFormat.R8G8B8_SRGB, (uint)view.Width, (uint)view.Height, 0, 90);
            }
            catch (UnityException)
            {
                // Written on the main thread instead.
                prepared.FullJpeg = prepared.ViewJpeg = null;
                prepared.FullRows = fullRows;
                prepared.FullWidth = picture.Width;
                prepared.FullHeight = picture.Height;
            }
            return prepared;
        }

        private static byte[] EncodeJpeg(byte[] rows, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.SetPixelData(rows, 0);
                texture.Apply(false, false);
                return texture.EncodeToJPG(90);
            }
            finally
            {
                Destroy(texture);
            }
        }

        /// <summary>A new picture of a tile: kept in the project (with a smaller copy when it is large), then laid over the ground.</summary>
        private bool ShowPicture(OmsiTileSummary tile, PreparedPicture prepared, OmsiImageryMeta meta)
        {
            if (groundRenderer == null || prepared == null)
                return false;
            try
            {
                if (prepared.FullJpeg == null)
                {
                    prepared.FullJpeg = EncodeJpeg(prepared.FullRows, prepared.FullWidth, prepared.FullHeight);
                    if (prepared.Larger)
                        prepared.ViewJpeg = EncodeJpeg(prepared.ViewRows, prepared.ViewWidth, prepared.ViewHeight);
                }
                session.SaveImagery(tile, prepared.FullJpeg, meta, prepared.Larger ? prepared.ViewJpeg : null);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                Debug.LogException(exception);
                SetStatus("The picture of tile " + tile.X + ", " + tile.Y + " could not be kept: " + exception.Message);
            }
            var texture = new Texture2D(prepared.ViewWidth, prepared.ViewHeight, TextureFormat.RGB24, true)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Imagery " + tile.X + ", " + tile.Y
            };
            texture.SetPixelData(prepared.ViewRows, 0);
            texture.Apply(true, false);
            LayPicture(tile, texture, meta);
            return true;
        }

        /// <summary>The terrain of the tiles beside the loaded heights bent to meet them, as one step.</summary>
        private void FitSteppedEdges()
        {
            if (steppedEdges.Count == 0 || currentSummary == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("fit tile edges"));
                return;
            }
            OmsiTerrainChange change;
            try
            {
                change = session.FitTerrainEdges(currentSummary, steppedEdges);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatus("The tile edges could not be fitted: " + exception.Message);
                return;
            }
            steppedEdges = new List<OmsiTerrainEdge>();
            satelliteWindow?.ShowSteppedEdges(0);
            if (change == null)
            {
                SetStatus("The tile edges meet already.");
                return;
            }
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = "the fitted tile edges" });
            RefreshGround(change.Tiles);
            SetStatus("Fitted the edges of " + change.Tiles.Count + " tile(s) to the loaded heights. Ctrl+Z takes it back.");
        }

        private void ReadImageryPreferences()
        {
            try
            {
                var path = OmsiImageryPreferences.DefaultPath;
                imageryPreferences = File.Exists(path) ? OmsiImageryPreferences.Parse(File.ReadAllText(path)) : new OmsiImageryPreferences();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("The imagery keys and servers could not be read: " + exception.Message);
                imageryPreferences = new OmsiImageryPreferences();
            }
        }

        private void SaveImageryPreferences()
        {
            try
            {
                var path = OmsiImageryPreferences.DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, imageryPreferences.ToString());
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus("The imagery keys and servers could not be saved: " + exception.Message);
            }
        }

        /// <summary>The sources in the window's list, with the keys and the own servers.</summary>
        private void ShowImagerySources()
        {
            if (satelliteWindow == null)
                return;
            var all = imageryPreferences.All();
            satelliteWindow.SetSources(all);
            satelliteWindow.ShowKeys(all, name => imageryPreferences.Key(name));
        }

        /// <summary>
        /// Zoomed in closer than a tile's copy shows, the tiles near the middle of the view get their full picture, one each
        /// call, at most a few; tiles that left the view go back to their copy. Called a few times a second.
        /// </summary>
        private void TickSharpImagery()
        {
            if (groundRenderer == null || mapCanvas == null || currentSummary == null || session?.Workspace == null || !mapCanvas.HasView)
                return;
            if (Time.realtimeSinceStartup - sharpCheckedAt < 0.4f)
                return;
            sharpCheckedAt = Time.realtimeSinceStartup;
            var tileSize = currentSummary.TileSizeMeters;
            double centreX, centreY, metresPerPixel, reach;
            if (mapCanvas.ThreeDimensional)
            {
                centreX = mapCanvas.OrbitTarget.x + mapCanvas.OriginX;
                centreY = mapCanvas.OrbitTarget.y + mapCanvas.OriginY;
                // About what a screen pixel covers at the target, for the camera's field of view.
                metresPerPixel = mapCanvas.OrbitDistance * 0.0013;
                reach = mapCanvas.OrbitDistance * 1.2;
            }
            else
            {
                centreX = mapCanvas.Center.x + mapCanvas.OriginX;
                centreY = mapCanvas.Center.y + mapCanvas.OriginY;
                metresPerPixel = mapCanvas.MetersPerPixel;
                var bounds = mapCanvas.worldBound;
                reach = 0.5 * Math.Sqrt(bounds.width * bounds.width + bounds.height * bounds.height) * metresPerPixel;
            }
            var wanted = new List<KeyValuePair<double, OmsiTileSummary>>();
            if (groundRenderer.ImageryVisible && metresPerPixel < tileSize / ImageryPictureSize * 0.9)
                foreach (var tile in currentSummary.Tiles)
                {
                    if (!tile.IsAvailable || HasOwnPicture(tile) || !session.Workspace.IsModified(OmsiMapSession.ImageryViewPath(tile)))
                        continue;
                    var dx = (tile.X + 0.5) * tileSize - centreX;
                    var dy = (tile.Y + 0.5) * tileSize - centreY;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance < reach + tileSize * 0.75)
                        wanted.Add(new KeyValuePair<double, OmsiTileSummary>(distance, tile));
                }
            wanted.Sort((a, b) => a.Key.CompareTo(b.Key));
            if (wanted.Count > SharpTileLimit)
                wanted.RemoveRange(SharpTileLimit, wanted.Count - SharpTileLimit);
            foreach (var sharp in groundRenderer.SharpTiles())
                if (!wanted.Exists(pair => pair.Value.X == sharp.Key && pair.Value.Y == sharp.Value))
                    groundRenderer.SetSharpImagery(sharp.Key, sharp.Value, null);
            var next = wanted.Find(pair => !groundRenderer.HasSharpImagery(pair.Value.X, pair.Value.Y)).Value;
            if (next == null)
                return;
            var bytes = session.ReadImagery(next, out _);
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, true)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Sharp imagery " + next.X + ", " + next.Y
            };
            if (bytes == null || !texture.LoadImage(bytes, false))
            {
                Destroy(texture);
                return;
            }
            texture.Compress(false);
            texture.Apply(false, true);
            groundRenderer.SetSharpImagery(next.X, next.Y, texture);
        }

        private void LayPicture(OmsiTileSummary tile, Texture2D texture, OmsiImageryMeta meta)
        {
            // An own picture on the tile stays on top; the picture from the air waits in the project.
            if (HasOwnPicture(tile))
            {
                Destroy(texture);
                return;
            }
            texture.Compress(false);
            texture.Apply(false, true);
            groundRenderer.SetImagery(tile.X, tile.Y, texture);
            imagerySources[MapTileKey(tile)] = meta?.Source;
            UpdateImageryAttribution();
        }

        private OmsiImagerySource FindImagerySource(string name)
        {
            foreach (var source in imageryPreferences.All())
                if (source.Name == name)
                    return source;
            return name == OmsiImagerySource.OsmMap.Name ? OmsiImagerySource.OsmMap : null;
        }

        private OmsiTrueTypeFont osmFont;
        private bool osmFontRead;

        /// <summary>Inter's letters for the names on the drawn OpenStreetMap map, kept beside the interface's font.</summary>
        private OmsiTrueTypeFont OsmFont()
        {
            if (osmFontRead)
                return osmFont;
            osmFontRead = true;
            var asset = Resources.Load<TextAsset>("Fonts/Inter-Regular-Outlines");
            try
            {
                osmFont = asset != null ? new OmsiTrueTypeFont(asset.bytes) : null;
            }
            catch (InvalidDataException exception)
            {
                Debug.LogWarning("The font for map names cannot be read: " + exception.Message);
            }
            if (osmFont == null)
                Debug.LogWarning("The OpenStreetMap map is drawn without names: Fonts/Inter-Regular-Outlines is missing.");
            return osmFont;
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
            steppedEdges = new List<OmsiTerrainEdge>();
            satelliteWindow?.ShowSteppedEdges(0);
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
            ownPictures = new List<OmsiOwnPicture>();
            ownPictureTiles.Clear();
            if (session?.Workspace == null || currentSummary == null)
                return;
            foreach (var tile in currentSummary.Tiles)
                if (tile.IsAvailable && session.Workspace.IsModified(OmsiMapSession.ImageryPath(tile)))
                    storedImagery.Enqueue(tile);
            LoadOwnPictures();
            int west = int.MaxValue, south = int.MaxValue, east = int.MinValue, north = int.MinValue;
            foreach (var tile in currentSummary.Tiles)
                if (tile.IsAvailable)
                {
                    west = Math.Min(west, tile.X);
                    south = Math.Min(south, tile.Y);
                    east = Math.Max(east, tile.X);
                    north = Math.Max(north, tile.Y);
                }
            if (west <= east)
                satelliteWindow?.SetPictureTiles(west, south, east, north);
        }

        /// <summary>Runs what the background work handed to the main thread, and lays stored pictures, within a few milliseconds.</summary>
        private void PumpAerial()
        {
            var started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 0.012f && aerialMainThread.TryDequeue(out var action))
                action();
            TickOwnPictures();
            TickSharpImagery();
            while (Time.realtimeSinceStartup - started < 0.012f && storedImagery.Count > 0)
            {
                var tile = storedImagery.Dequeue();
                if (session == null)
                    break;
                var bytes = session.ReadImageryForView(tile, out var meta, out _);
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
        /// a tile; -satelliteLoad all|tile|heights [-satelliteHeights] loads and waits for the end; -satelliteFitEdges fits the
        /// edges beside loaded heights; -satelliteSize 0|1024|2048|4096 sets the picture size; -satelliteOpacity &lt;0..1&gt;.
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
            if (int.TryParse(Argument("-satelliteSize", 1), out var pictureSize) && geoSettings != null)
            {
                geoSettings.PictureSize = pictureSize;
                SaveGeo();
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
            // -pictureLoad <file> [west south east north] lays an own picture; -pictureFit <turn> <east> <north> <size> lines it up.
            var pictureFile = Argument("-pictureLoad", 1);
            if (pictureFile != null)
            {
                if (int.TryParse(Argument("-pictureLoad", 2), out var pictureWest) && int.TryParse(Argument("-pictureLoad", 3), out var pictureSouth) &&
                    int.TryParse(Argument("-pictureLoad", 4), out var pictureEast) && int.TryParse(Argument("-pictureLoad", 5), out var pictureNorth))
                    LoadOwnPicture(pictureWest, pictureSouth, pictureEast, pictureNorth, pictureFile);
                else
                    LoadOwnPicture(0, 0, 0, 0, pictureFile);
                if (double.TryParse(Argument("-pictureFit", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var turn) &&
                    double.TryParse(Argument("-pictureFit", 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var east) &&
                    double.TryParse(Argument("-pictureFit", 3), NumberStyles.Float, CultureInfo.InvariantCulture, out var north) &&
                    double.TryParse(Argument("-pictureFit", 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                {
                    var fit = ownPictures.Find(item => item.Id == selectedPictureId)?.Clone();
                    if (fit != null)
                    {
                        fit.Rotation = turn;
                        fit.OffsetX = east;
                        fit.OffsetY = north;
                        fit.Scale = size;
                        ChangePictureFit(fit);
                    }
                }
                var cutWait = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - cutWait < 3f)
                    yield return null;
                LogSatellite();
            }
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
                if (steppedEdges.Count > 0)
                    Debug.Log("Satellite: " + steppedEdges.Count + " stepped edge(s) beside the loaded heights.");
            }
            // -satelliteFitEdges bends the tiles beside loaded heights to meet them.
            if (Array.IndexOf(args, "-satelliteFitEdges") >= 0)
            {
                FitSteppedEdges();
                LogSatellite();
            }
            // Zoomed in, the full pictures of the tiles in view come a few times a second.
            var sharpWait = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - sharpWait < 5f)
                yield return null;
            Debug.Log("Satellite: " + groundRenderer.SharpTiles().Count + " tile(s) with their full picture.");
        }
    }
}
