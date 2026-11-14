using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>Painting the ground textures of a map with the terrain tool's brush (ADR 091).</summary>
    public sealed partial class EditorShellBootstrap
    {
        private const int GroundThumbnailSize = 96;

        private OmsiPaintStroke paintStroke;
        private readonly Dictionary<string, Texture2D> groundThumbnails = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Wires the paint options of the terrain tool.</summary>
        private void ConnectGroundPaint()
        {
            terrainOptions.PaintingChanged += () =>
            {
                ReleaseTerrain();
                if (terrainOptions.Painting)
                    RefreshPaintTextures();
            };
            terrainOptions.Paint.ArmedChanged += OnTileModeChanged;
            terrainOptions.Paint.AddConfirmed += AddGroundTexture;
        }

        /// <summary>The pointer's tool and the hint follow the armed tile mode; no mode: the brush ring and its hint are back.</summary>
        private void OnTileModeChanged()
        {
            var armed = terrainOptions.Paint.Armed;
            if (armed != PaintTileArm.None)
                ReleaseTerrain();
            mapCanvas?.SetPointerBadge(armed == PaintTileArm.Cover ? "paint-bucket" : armed == PaintTileArm.Clear ? "square-x" : null);
            if (armed == PaintTileArm.None)
                terrainOptions.ShowPlan(string.Empty, false);
            if (activeTool == EditorTool.Terrain)
                ShowTerrainBrush();
        }

        /// <summary>
        /// While a tile mode is armed: the tile under the pointer is outlined instead of the brush ring, and the hint says what a click does.
        /// </summary>
        private void ShowTileModeBrush()
        {
            var armed = terrainOptions.Paint.Armed;
            var texture = terrainOptions.Paint.TextureName;
            terrainOptions.ShowPlan(armed == PaintTileArm.Cover ? Loc.T("paint.hint.cover", ("texture", texture)) : Loc.T("paint.hint.clear", ("texture", texture)), false);
            var tile = roadPointer.HasValue ? TileAt(roadPointer.Value.X, roadPointer.Value.Y) : null;
            if (tile == null)
            {
                mapCanvas.ClearRoadPreview();
                return;
            }
            var size = currentSummary.TileSizeMeters;
            double west = tile.X * size, south = tile.Y * size, east = west + size, north = south + size;
            mapCanvas.SetRoadPreview(new List<OmsiMapPoint[]>
            {
                new[]
                {
                    new OmsiMapPoint(west, south), new OmsiMapPoint(east, south), new OmsiMapPoint(east, north), new OmsiMapPoint(west, north),
                    new OmsiMapPoint(west, south)
                }
            }, true, null, null, null);
        }

        private OmsiTileSummary TileAt(double x, double y)
        {
            var size = currentSummary.TileSizeMeters;
            var tileX = (int)Math.Floor(x / size);
            var tileY = (int)Math.Floor(y / size);
            return currentSummary.Tiles.Find(tile => tile.IsAvailable && tile.X == tileX && tile.Y == tileY);
        }

        /// <summary>A click in the map while a tile mode is armed: that tile is covered or cleared as one step; the mode stays on.</summary>
        private void ClickTileMode(double x, double y)
        {
            CoverTile(TileAt(x, y), terrainOptions.Paint.Texture, terrainOptions.Paint.Armed == PaintTileArm.Cover);
            ShowTileModeBrush();
        }

        /// <summary>Lists the map's ground textures and the files a new one can be chosen from.</summary>
        private void RefreshPaintTextures()
        {
            if (session == null || currentSummary == null || terrainOptions == null || !CanEdit)
                return;
            var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var choices = new List<GroundTextureChoice>();
            try
            {
                foreach (var texture in session.ReadGroundTextures())
                {
                    var name = Path.GetFileNameWithoutExtension(texture.TexturePath.Replace('\\', '/'));
                    choices.Add(new GroundTextureChoice
                    {
                        Index = texture.Index,
                        Name = texture.Index == 0 ? Loc.T("paint.base", ("name", name)) : name,
                        Thumbnail = GroundThumbnail(installation, texture.TexturePath)
                    });
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
            }
            terrainOptions.Paint.SetTextures(choices);
            var files = new List<string>();
            try
            {
                var folder = installation == null ? null : Path.Combine(installation, "Texture");
                if (folder != null && Directory.Exists(folder))
                    foreach (var pattern in new[] { "*.bmp", "*.tga", "*.dds" })
                        foreach (var file in Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly))
                            files.Add("texture\\" + Path.GetFileName(file));
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
            }
            terrainOptions.Paint.SetAvailable(files);
        }

        /// <summary>
        /// A small picture of a ground texture for its tile on the Paint tab, or null when the file cannot be read. The pictures are made once
        /// from the installation's file (the ground renderer keeps only its GPU textures) and kept, so opening the tab again reads nothing.
        /// </summary>
        private Texture2D GroundThumbnail(string installation, string relativePath)
        {
            if (installation == null || string.IsNullOrEmpty(relativePath))
                return null;
            try
            {
                var path = OmsiRelativePath.Resolve(installation, relativePath);
                if (groundThumbnails.TryGetValue(path, out var kept) && kept != null)
                    return kept;
                if (!File.Exists(path) || !OmsiImageReader.TryDecode(File.ReadAllBytes(path), out var image) || image.Width < 1 || image.Height < 1)
                    return null;
                var size = GroundThumbnailSize;
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                {
                    var fromY = y * image.Height / size;
                    var toY = Math.Max(fromY + 1, (y + 1) * image.Height / size);
                    var stepY = Math.Max(1, (toY - fromY) / 4);
                    for (var x = 0; x < size; x++)
                    {
                        var fromX = x * image.Width / size;
                        var toX = Math.Max(fromX + 1, (x + 1) * image.Width / size);
                        var stepX = Math.Max(1, (toX - fromX) / 4);
                        long red = 0, green = 0, blue = 0, count = 0;
                        for (var sy = fromY; sy < toY; sy += stepY)
                            for (var sx = fromX; sx < toX; sx += stepX)
                            {
                                var at = (sy * image.Width + sx) * image.Channels;
                                red += image.Pixels[at];
                                green += image.Channels >= 3 ? image.Pixels[at + 1] : image.Pixels[at];
                                blue += image.Channels >= 3 ? image.Pixels[at + 2] : image.Pixels[at];
                                count++;
                            }
                        // The picture's rows run top to bottom; a Unity texture's first row is its bottom.
                        pixels[(size - 1 - y) * size + x] = new Color32((byte)(red / count), (byte)(green / count), (byte)(blue / count), 255);
                    }
                }
                var thumbnail = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                thumbnail.SetPixels32(pixels);
                thumbnail.Apply(false, true);
                groundThumbnails[path] = thumbnail;
                return thumbnail;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException ||
                                              exception is InvalidDataException)
            {
                return null;
            }
        }

        private void PressPaint()
        {
            ReleasePaint();
            if (!CanEdit)
                return;
            try
            {
                paintStroke = session.BeginPaintStroke();
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("paint.status.failed", ("reason", exception.Message));
                return;
            }
            terrainSince = TerrainStepSeconds;
        }

        private void TickPaint()
        {
            if (paintStroke == null || !roadPointer.HasValue || activeTool != EditorTool.Terrain)
                return;
            terrainSince += Time.unscaledDeltaTime;
            if (terrainSince < TerrainStepSeconds)
                return;
            terrainSince = 0.0;
            List<OmsiTileSummary> touched;
            try
            {
                touched = session.ApplyGroundPaint(currentSummary, paintStroke, terrainOptions.Paint.Texture, terrainOptions.Paint.PaintMode, new OmsiPaintDab
                {
                    X = roadPointer.Value.X, Y = roadPointer.Value.Y, Radius = terrainOptions.Radius, Hardness = terrainOptions.Paint.Hardness,
                    Strength = terrainOptions.Strength
                });
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("paint.status.failed", ("reason", exception.Message));
                ReleasePaint();
                return;
            }
            if (touched.Count > 0)
                groundRenderer?.RefreshMasks(touched);
        }

        /// <summary>The button let go: the stroke is kept as one step.</summary>
        private void ReleasePaint()
        {
            var stroke = paintStroke;
            paintStroke = null;
            if (stroke == null)
                return;
            var change = session.EndPaintStroke(stroke);
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("paint.step") });
            groundRenderer?.RefreshMasks(change.Tiles);
            SetStatusT("paint.status.painted", ("count", change.Tiles.Count));
        }

        /// <summary>Covers the tile with the chosen texture, or takes the texture off it.</summary>
        private void CoverTile(OmsiTileSummary tile, int texture, bool covered)
        {
            if (tile == null)
            {
                SetStatusT("paint.status.no_tile");
                return;
            }
            OmsiTerrainChange change;
            try
            {
                change = session.CoverGroundTile(currentSummary, tile, texture, covered);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("paint.status.failed", ("reason", exception.Message));
                return;
            }
            if (change == null)
                return;
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("paint.step.cover", ("x", tile.X), ("y", tile.Y)) });
            groundRenderer?.RefreshMasks(change.Tiles);
            SetStatusT("paint.status.painted", ("count", change.Tiles.Count));
        }

        /// <summary>Adds a ground texture to the map; the map's ground is built anew for it.</summary>
        private void AddGroundTexture(string texture, string detail, int exponent, double repeats, double detailRepeats)
        {
            if (!CanEdit || session == null)
                return;
            OmsiTerrainChange change;
            try
            {
                change = session.AddGroundTexture(texture, detail, exponent, repeats, detailRepeats);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatusT("paint.status.failed", ("reason", exception.Message));
                return;
            }
            PushHistory(new ObjectEditStep { TerrainChange = change, Description = Loc.T("paint.step.add") });
            ReloadGround();
            RefreshPaintTextures();
            var textures = session.ReadGroundTextures();
            if (textures.Count > 0)
                terrainOptions.Paint.Choose(textures[textures.Count - 1].Index);
            SetStatusT("paint.status.added");
        }

        /// <summary>Builds the ground anew, after the list of its textures changed.</summary>
        private void ReloadGround()
        {
            if (groundRenderer == null || mapCanvas == null || currentSummary == null)
                return;
            var installationRoot = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            groundRenderer.Load(currentSummary, session.Source, Path.GetFileName(session.GlobalConfigPath), installationRoot, mapCanvas.OriginX, mapCanvas.OriginY);
            groundRenderer.RefreshWater(currentSummary);
        }

        /// <summary>After undo or redo of a painting step: the masks of its tiles are read again, or the whole ground when its textures changed.</summary>
        private void RefreshAfterPaintStep(OmsiTerrainChange change)
        {
            if (change.TexturesChanged)
            {
                ReloadGround();
                if (terrainOptions != null && terrainOptions.Painting)
                    RefreshPaintTextures();
            }
            else if (change.TimetableFiles)
            {
                StartRoutesBuild(currentSummary);
                RefreshVehiclesIfOpen();
            }
            else
                groundRenderer?.RefreshMasks(change.Tiles);
        }

        /// <summary>Texture, kind and hardness of the brush from the command line, for checks: -paintTexture N, -paintMode paint|erase|soften, -paintHardness 0-1, -paintAddForm.</summary>
        private void ApplyPaintArguments(Func<string, int, string> Argument)
        {
            if (terrainOptions == null || !terrainOptions.Painting)
                return;
            RefreshPaintTextures();
            // -paintAdd <texture> [<detail>] [<mask size exponent>]: adds a ground texture before painting.
            var added = Argument("-paintAdd", 1);
            if (added != null)
                AddGroundTexture(added, Argument("-paintAdd", 2) ?? string.Empty,
                    int.TryParse(Argument("-paintAdd", 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var exponent) ? exponent : 9, 1.0, 40.0);
            // -paintAddForm: opens the form of the add tile, in place of the tiles.
            if (Argument("-paintAddForm", 0) != null)
                terrainOptions.Paint.OpenAddForm();
            if (int.TryParse(Argument("-paintTexture", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var texture))
            {
                // The list only offers textures the map has; a number it has not is refused here as it cannot be clicked.
                if (terrainOptions.Paint.HasTexture(texture))
                    terrainOptions.Paint.Choose(texture);
                else
                    Debug.LogWarning("-paintTexture " + texture + ": the map has no such ground texture, the choice stays.");
            }
            if (Enum.TryParse<OmsiPaintMode>(Argument("-paintMode", 1), true, out var mode))
                terrainOptions.Paint.SetPaintMode(mode);
            if (double.TryParse(Argument("-paintHardness", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var hardness))
                terrainOptions.Paint.SetHardness(hardness);
        }
    }
}
