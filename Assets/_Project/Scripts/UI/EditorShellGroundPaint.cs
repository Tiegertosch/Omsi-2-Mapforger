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
        private OmsiPaintStroke paintStroke;

        /// <summary>Wires the paint options of the terrain tool.</summary>
        private void ConnectGroundPaint()
        {
            terrainOptions.PaintingChanged += () =>
            {
                ReleaseTerrain();
                if (terrainOptions.Painting)
                    RefreshPaintTextures();
            };
            terrainOptions.Paint.CoverRequested += CoverTileUnderPointer;
            terrainOptions.Paint.AddConfirmed += AddGroundTexture;
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
                        Color = AverageColor(installation, texture.TexturePath)
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

        /// <summary>The average colour of a texture, as the swatch in the list; grey when it cannot be read.</summary>
        private static Color AverageColor(string installation, string relativePath)
        {
            var grey = new Color(0.45f, 0.45f, 0.45f);
            try
            {
                if (installation == null)
                    return grey;
                var path = OmsiRelativePath.Resolve(installation, relativePath);
                if (!File.Exists(path) || !OmsiImageReader.TryDecode(File.ReadAllBytes(path), out var image) || image.Channels < 3)
                    return grey;
                long red = 0, green = 0, blue = 0, count = 0;
                var step = Math.Max(1, image.Width * image.Height / 4000);
                for (var pixel = 0; pixel < image.Width * image.Height; pixel += step)
                {
                    red += image.Pixels[pixel * image.Channels];
                    green += image.Pixels[pixel * image.Channels + 1];
                    blue += image.Pixels[pixel * image.Channels + 2];
                    count++;
                }
                return count == 0 ? grey : new Color(red / (255f * count), green / (255f * count), blue / (255f * count));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException ||
                                              exception is InvalidDataException)
            {
                return grey;
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

        /// <summary>Covers the tile under the pointer with the chosen texture, or takes the texture off it.</summary>
        private void CoverTileUnderPointer(bool covered)
        {
            if (!CanEdit || currentSummary == null || !roadPointer.HasValue)
            {
                SetStatusT("paint.status.no_tile");
                return;
            }
            var size = currentSummary.TileSizeMeters;
            var x = (int)Math.Floor(roadPointer.Value.X / size);
            var y = (int)Math.Floor(roadPointer.Value.Y / size);
            CoverTile(currentSummary.Tiles.Find(tile => tile.IsAvailable && tile.X == x && tile.Y == y), terrainOptions.Paint.Texture, covered);
        }

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
                StartRoutesBuild(currentSummary);
            else
                groundRenderer?.RefreshMasks(change.Tiles);
        }

        /// <summary>Texture, kind and hardness of the brush from the command line, for checks: -paintTexture N, -paintMode paint|erase|soften, -paintHardness 0-1.</summary>
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
            if (int.TryParse(Argument("-paintTexture", 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var texture))
                terrainOptions.Paint.Choose(texture);
            if (Enum.TryParse<OmsiPaintMode>(Argument("-paintMode", 1), true, out var mode))
                terrainOptions.Paint.SetPaintMode(mode);
            if (double.TryParse(Argument("-paintHardness", 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var hardness))
                terrainOptions.Paint.SetHardness(hardness);
        }
    }
}
