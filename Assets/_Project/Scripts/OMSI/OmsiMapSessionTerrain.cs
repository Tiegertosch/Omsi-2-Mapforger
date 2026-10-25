using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The ground shaped by hand: the shaped ground of every tile it touched before and after, so the step can be taken
    /// back and done again; the ground fitted to roads follows from it.
    /// </summary>
    /// <summary>
    /// An edge between tiles with loaded heights and a tile without: the tile beside, which way it lies from the loaded one,
    /// and the edge's 61 heights before the heights were laid.
    /// </summary>
    public sealed class OmsiTerrainEdge
    {
        public int TileX { get; set; }
        public int TileY { get; set; }

        /// <summary>Where the tile lies from the loaded one: (1, 0) east, (0, 1) north.</summary>
        public int DirectionX { get; set; }
        public int DirectionY { get; set; }

        public float[] Before { get; set; }

        /// <summary>The map-wide sample of the edge's sample <paramref name="index"/>, <paramref name="depth"/> samples into the tile.</summary>
        public void Sample(int index, int depth, out long gx, out long gy)
        {
            const int cells = OmsiTerrain.Resolution - 1;
            long originX = (long)TileX * cells, originY = (long)TileY * cells;
            if (DirectionX == 1)
            {
                gx = originX + depth;
                gy = originY + index;
            }
            else if (DirectionX == -1)
            {
                gx = originX + cells - depth;
                gy = originY + index;
            }
            else if (DirectionY == 1)
            {
                gx = originX + index;
                gy = originY + depth;
            }
            else
            {
                gx = originX + index;
                gy = originY + cells - depth;
            }
        }
    }

    public sealed class OmsiTerrainChange
    {
        internal OmsiTerrainChange()
        {
        }

        public bool IsApplied { get; internal set; } = true;

        /// <summary>The tiles whose ground changed, to draw again.</summary>
        public List<OmsiTileSummary> Tiles { get; } = new List<OmsiTileSummary>();

        internal List<OmsiObjectChange.FileState> Files { get; } = new List<OmsiObjectChange.FileState>();
    }

    /// <summary>
    /// A brush stroke or ramp in progress: the ground as shown and the shaped ground beneath it, changed alike, until
    /// <see cref="OmsiMapSession.EndTerrainStroke"/> keeps it as one step.
    /// </summary>
    public sealed class OmsiTerrainStroke
    {
        internal OmsiTerrainStroke(OmsiTerrainGrid shown, OmsiTerrainGrid shaped)
        {
            Shown = shown;
            Shaped = shaped;
        }

        /// <summary>The ground as the map shows it, roads fitted in; brushes work on it.</summary>
        internal OmsiTerrainGrid Shown { get; }

        /// <summary>The ground shaped by hand, before roads are fitted in; it takes the same changes.</summary>
        internal OmsiTerrainGrid Shaped { get; }

        /// <summary>Where Flatten works towards: the height where the stroke started, or a height typed.</summary>
        public double FlattenTo { get; set; }

        /// <summary>The water's surface for the Water brush: the tile's water where the stroke started, else 0.3 m under the ground there.</summary>
        public double WaterLevel { get; set; }

        /// <summary>How deep the Water brush digs under the surface.</summary>
        public double WaterDepth { get; set; } = 1.5;

        /// <summary>The tiles the Water brush went over, which get water at its surface when they have none.</summary>
        internal HashSet<OmsiTileSummary> WaterTiles { get; } = new HashSet<OmsiTileSummary>();

        /// <summary>The ground's height as shown at a world position; null outside the terrain.</summary>
        public double? HeightAt(double x, double y)
        {
            return Shown.HeightAt(x, y);
        }
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Where the ground shaped by hand of a tile is kept in the project: beside the map's files in the workspace, left
        /// out of exports. Without it, the original map's ground counts as shaped.
        /// </summary>
        internal const string ShapedTerrainFolder = OmsiMapExporter.ProjectDataFolder + "\\Terrain";

        private static string ShapedTerrainPath(string terrainPath)
        {
            return ShapedTerrainFolder + "\\" + terrainPath;
        }

        /// <summary>
        /// The ground a tile's roads are fitted into: the ground shaped by hand, else the original map's; for tiles added
        /// in the editor, which have no original, the ground the workspace holds when <paramref name="orCurrent"/> is set,
        /// else null.
        /// </summary>
        private byte[] ShapedTerrainBytes(string terrainPath, bool orCurrent = false)
        {
            var workspace = RequireWorkspace();
            var shaped = ShapedTerrainPath(terrainPath);
            if (workspace.IsModified(shaped))
                return workspace.ReadAllBytes(shaped);
            var original = workspace.ReadOriginalBytes(terrainPath);
            if (original != null || !orCurrent)
                return original;
            return workspace.FileExists(terrainPath) ? workspace.ReadAllBytes(terrainPath) : null;
        }

        /// <summary>Starts a brush stroke or ramp; nothing is kept until <see cref="EndTerrainStroke"/>.</summary>
        public OmsiTerrainStroke BeginTerrainStroke(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var workspace = RequireWorkspace();
            var shown = new OmsiTerrainGrid(summary, tile =>
            {
                var path = OmsiRelativePath.Normalize(tile.FileName) + ".terrain";
                return workspace.FileExists(path) ? OmsiTerrain.Parse(workspace.ReadAllBytes(path)).CopyHeights() : null;
            });
            var shaped = new OmsiTerrainGrid(summary, tile =>
            {
                var bytes = ShapedTerrainBytes(OmsiRelativePath.Normalize(tile.FileName) + ".terrain", true);
                return bytes != null ? OmsiTerrain.Parse(bytes).CopyHeights() : null;
            });
            return new OmsiTerrainStroke(shown, shaped);
        }

        /// <summary>
        /// The brush held over a world position for some seconds: the ground as shown changes as openMapper's brush
        /// changes it, never rising through a road beside it, and the shaped ground beneath by as much. The shown ground
        /// of the touched tiles is written at once, so the map can draw it; returns those tiles.
        /// </summary>
        public List<OmsiTileSummary> ApplyTerrainBrush(OmsiMapSummary summary, OmsiTerrainStroke stroke, OmsiTerrainBrushMode mode, double x, double y,
            double radius, double strength, double seconds)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (stroke == null)
                throw new ArgumentNullException(nameof(stroke));
            ShapeCatalogs();
            var roads = OmsiTerrainBrush.RoadPointsNear(summary, shapeProfiles, x, y, radius + 15.0);
            var before = Snapshot(stroke.Shown, x, y, radius);
            OmsiTerrainBrush.Stroke(stroke.Shown, mode, x, y, radius, strength, seconds, stroke.FlattenTo, roads, stroke.WaterLevel - stroke.WaterDepth);
            if (mode == OmsiTerrainBrushMode.Water)
                foreach (var point in new[] { new OmsiMapPoint(x, y), new OmsiMapPoint(x + radius, y), new OmsiMapPoint(x - radius, y), new OmsiMapPoint(x, y + radius),
                             new OmsiMapPoint(x, y - radius) })
                {
                    var tile = TileAt(summary, point.X, point.Y);
                    if (tile != null && tile.Water == null)
                        stroke.WaterTiles.Add(tile);
                }
            return Follow(stroke, before);
        }

        /// <summary>
        /// The ground between two world positions made one even slope from the shown height at the first to that at the
        /// second, as wide as the brush; returns the touched tiles and the gradient in percent.
        /// </summary>
        public List<OmsiTileSummary> ApplyTerrainRamp(OmsiTerrainStroke stroke, double ax, double ay, double bx, double by, double radius, out double gradientPercent)
        {
            if (stroke == null)
                throw new ArgumentNullException(nameof(stroke));
            gradientPercent = 0.0;
            var aHeight = stroke.Shown.HeightAt(ax, ay);
            var bHeight = stroke.Shown.HeightAt(bx, by);
            if (!aHeight.HasValue || !bHeight.HasValue)
                return new List<OmsiTileSummary>();
            var before = Snapshot(stroke.Shown, (ax + bx) / 2.0, (ay + by) / 2.0,
                Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) / 2.0 + radius + stroke.Shown.CellMeters);
            gradientPercent = OmsiTerrainBrush.Ramp(stroke.Shown, ax, ay, aHeight.Value, bx, by, bHeight.Value, radius);
            return Follow(stroke, before);
        }

        /// <summary>
        /// The ground under each object made level with its foot, as openMapper's "Level ground" does: its outline and 3 m
        /// around it ease into the ground about, at the ground's height under its middle, or at its own height when that
        /// is absolute. Trees and objects without an outline are left out. One step, or null when nothing changed;
        /// <paramref name="levelled"/> says for how many objects.
        /// </summary>
        public OmsiTerrainChange LevelGroundUnder(OmsiMapSummary summary, IEnumerable<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>> objects, out int levelled)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (objects == null)
                throw new ArgumentNullException(nameof(objects));
            levelled = 0;
            ShapeCatalogs();
            var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(GlobalConfigPath)));
            var stroke = BeginTerrainStroke(summary);
            var size = summary.TileSizeMeters;
            foreach (var pair in objects)
            {
                var item = pair.Value;
                var outline = shapeOutlines.Get(item.AssetPath);
                if (outline.Source == OmsiOutlineSource.Tree || outline.Points.Length == 0)
                    continue;
                var min = new OmsiMapPoint(double.MaxValue, double.MaxValue);
                var max = new OmsiMapPoint(double.MinValue, double.MinValue);
                foreach (var point in outline.Points)
                {
                    min = new OmsiMapPoint(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y));
                    max = new OmsiMapPoint(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y));
                }
                var x = pair.Key.X * size + item.LocalX;
                var y = pair.Key.Y * size + item.LocalY;
                var ground = stroke.HeightAt(x, y);
                if (!ground.HasValue)
                    continue;
                var height = HasAbsoluteHeight(installation, item.AssetPath) ? item.Height : ground.Value;
                var reach = Math.Sqrt((max.X - min.X) * (max.X - min.X) + (max.Y - min.Y) * (max.Y - min.Y)) +
                            Math.Sqrt((min.X + max.X) * (min.X + max.X) + (min.Y + max.Y) * (min.Y + max.Y)) + 4.0;
                var before = Snapshot(stroke.Shown, x, y, reach);
                OmsiTerrainBrush.Level(stroke.Shown, x, y, item.HeadingDegrees, min, max, height);
                Follow(stroke, before);
                levelled++;
            }
            return EndTerrainStroke(summary, stroke);
        }

        /// <summary>The ground as the map shows it, as a height map (ADR 040, stage 3).</summary>
        public OmsiHeightMapExport ExportHeightMap(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var name = string.IsNullOrWhiteSpace(summary.FriendlyName) ? summary.Name : summary.FriendlyName;
            return OmsiHeightMap.Export(summary, BeginTerrainStroke(summary).Shown, name);
        }

        /// <summary>
        /// A height map laid over the ground shaped by hand of all tiles, replacing it or added to it; the roads built with
        /// terrain fitting are fitted into it again. One step, or null when nothing changed.
        /// </summary>
        public OmsiTerrainChange ImportHeightMap(OmsiMapSummary summary, OmsiGreyPicture picture, OmsiHeightRange range, bool add)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (picture == null)
                throw new ArgumentNullException(nameof(picture));
            var stroke = BeginTerrainStroke(summary);
            OmsiHeightMap.Import(summary, stroke.Shaped, picture, range, add);
            return EndTerrainStroke(summary, stroke);
        }

        /// <summary>
        /// Terrain heights from the world laid on tiles, 61 × 61 each from the south, taking the place of the ground shaped
        /// by hand; roads fitted to the terrain are fitted in again. One step, or null when nothing changed.
        /// </summary>
        /// <param name="steppedEdges">
        /// Filled with the edges the heights share with tiles that got none, with their heights before: the ground steps one
        /// sample inside those tiles until <see cref="FitTerrainEdges"/> bends them.
        /// </param>
        public OmsiTerrainChange ImportTerrainHeights(OmsiMapSummary summary, IEnumerable<KeyValuePair<OmsiTileSummary, float[]>> heights,
            List<OmsiTerrainEdge> steppedEdges = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (heights == null)
                throw new ArgumentNullException(nameof(heights));
            const int cells = OmsiTerrain.Resolution - 1;
            var stroke = BeginTerrainStroke(summary);
            var laid = new List<KeyValuePair<OmsiTileSummary, float[]>>();
            foreach (var pair in heights)
                if (pair.Value != null && pair.Value.Length == OmsiTerrain.Resolution * OmsiTerrain.Resolution)
                    laid.Add(pair);
            if (steppedEdges != null)
            {
                var loaded = new HashSet<long>();
                foreach (var pair in laid)
                    loaded.Add(((long)pair.Key.X << 32) | (uint)pair.Key.Y);
                foreach (var pair in laid)
                    foreach (var direction in new[] { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } })
                    {
                        int x = pair.Key.X + direction[0], y = pair.Key.Y + direction[1];
                        var neighbour = summary.Tiles.Find(tile => tile.X == x && tile.Y == y && tile.IsAvailable && tile.HasTerrain);
                        if (neighbour == null || loaded.Contains(((long)x << 32) | (uint)y))
                            continue;
                        var edge = new OmsiTerrainEdge { TileX = x, TileY = y, DirectionX = direction[0], DirectionY = direction[1], Before = new float[OmsiTerrain.Resolution] };
                        for (var index = 0; index < OmsiTerrain.Resolution; index++)
                        {
                            edge.Sample(index, 0, out var gx, out var gy);
                            edge.Before[index] = stroke.Shaped.Get(gx, gy) ?? 0f;
                        }
                        steppedEdges.Add(edge);
                    }
            }
            foreach (var pair in laid)
                for (var row = 0; row < OmsiTerrain.Resolution; row++)
                    for (var column = 0; column < OmsiTerrain.Resolution; column++)
                        stroke.Shaped.Set((long)pair.Key.X * cells + column, (long)pair.Key.Y * cells + row, pair.Value[row * OmsiTerrain.Resolution + column]);
            // An edge that moved by less than 5 cm does not step: tiles loaded before meet the new ones.
            steppedEdges?.RemoveAll(edge =>
            {
                for (var index = 0; index < OmsiTerrain.Resolution; index++)
                {
                    edge.Sample(index, 0, out var gx, out var gy);
                    if (Math.Abs((stroke.Shaped.Get(gx, gy) ?? edge.Before[index]) - edge.Before[index]) >= 0.05f)
                        return false;
                }
                return true;
            });
            return EndTerrainStroke(summary, stroke);
        }

        /// <summary>
        /// Bends tiles beside loaded heights to meet them, as openMapper's fit_edge: each sample of the shared edge moved by
        /// as much as the edge moved, fading out smoothly over some samples into the tile (12: 60 m on a 300 m tile). One
        /// step, or null when nothing changed.
        /// </summary>
        public OmsiTerrainChange FitTerrainEdges(OmsiMapSummary summary, IEnumerable<OmsiTerrainEdge> edges, int reach = 12)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (edges == null)
                throw new ArgumentNullException(nameof(edges));
            reach = Math.Max(1, Math.Min(OmsiTerrain.Resolution - 1, reach));
            var stroke = BeginTerrainStroke(summary);
            foreach (var edge in edges)
                for (var index = 0; index < OmsiTerrain.Resolution; index++)
                {
                    edge.Sample(index, 0, out var edgeX, out var edgeY);
                    var now = stroke.Shaped.Get(edgeX, edgeY);
                    if (!now.HasValue)
                        continue;
                    var delta = now.Value - edge.Before[index];
                    if (Math.Abs(delta) < 1e-4f)
                        continue;
                    for (var depth = 1; depth < reach; depth++)
                    {
                        var t = depth / (float)reach;
                        // Smooth: no kink where the fade ends.
                        var weight = 1f - t * t * (3f - 2f * t);
                        edge.Sample(index, depth, out var gx, out var gy);
                        var height = stroke.Shaped.Get(gx, gy);
                        if (height.HasValue)
                            stroke.Shaped.Set(gx, gy, height.Value + delta * weight);
                    }
                }
            return EndTerrainStroke(summary, stroke);
        }

        /// <summary>The project's placement on the Earth and imagery settings (Mapforge\geo.txt), or the defaults.</summary>
        public OmsiGeoSettings ReadGeoSettings()
        {
            var workspace = RequireWorkspace();
            return workspace.IsModified(OmsiGeoSettings.RelativePath)
                ? OmsiGeoSettings.Parse(System.Text.Encoding.UTF8.GetString(workspace.ReadAllBytes(OmsiGeoSettings.RelativePath)))
                : new OmsiGeoSettings();
        }

        public void SaveGeoSettings(OmsiGeoSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            RequireWorkspace().Save(OmsiGeoSettings.RelativePath, System.Text.Encoding.UTF8.GetBytes(settings.ToString()));
        }

        /// <summary>Where a tile's imagery is kept in the project: Mapforge\Imagery\tile_x_y.map.jpg, never exported.</summary>
        public static string ImageryPath(OmsiTileSummary tile)
        {
            return OmsiMapExporter.ProjectDataFolder + "\\Imagery\\" + System.IO.Path.GetFileName(OmsiRelativePath.Normalize(tile.FileName)) + ".jpg";
        }

        /// <summary>Where a tile's smaller copy for the view is kept, when its picture is larger than the view shows.</summary>
        public static string ImageryViewPath(OmsiTileSummary tile)
        {
            var path = ImageryPath(tile);
            return path.Substring(0, path.Length - 4) + ".view.jpg";
        }

        /// <summary>
        /// Keeps a tile's imagery (a JPEG), what it was made from, and a smaller copy for the view when the picture is
        /// larger (null when it is not: an older copy goes).
        /// </summary>
        public void SaveImagery(OmsiTileSummary tile, byte[] jpeg, OmsiImageryMeta meta, byte[] viewJpeg = null)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (jpeg == null)
                throw new ArgumentNullException(nameof(jpeg));
            var workspace = RequireWorkspace();
            workspace.Save(ImageryPath(tile), jpeg);
            workspace.Save(ImageryPath(tile) + ".txt", System.Text.Encoding.UTF8.GetBytes(meta?.ToString() ?? string.Empty));
            if (viewJpeg != null)
                workspace.Save(ImageryViewPath(tile), viewJpeg);
            else if (workspace.IsModified(ImageryViewPath(tile)))
                workspace.Revert(ImageryViewPath(tile));
        }

        /// <summary>A tile's picture for the view: its smaller copy when it has one, else the picture; null when it has none.</summary>
        public byte[] ReadImageryForView(OmsiTileSummary tile, out OmsiImageryMeta meta, out bool sharper)
        {
            var full = ReadImagery(tile, out meta);
            sharper = full != null && Workspace.IsModified(ImageryViewPath(tile));
            return sharper ? Workspace.ReadAllBytes(ImageryViewPath(tile)) : full;
        }

        /// <summary>A tile's imagery (a JPEG), or null when it has none.</summary>
        public byte[] ReadImagery(OmsiTileSummary tile, out OmsiImageryMeta meta)
        {
            meta = null;
            if (tile == null || Workspace == null)
                return null;
            var path = ImageryPath(tile);
            if (!Workspace.IsModified(path))
                return null;
            if (Workspace.IsModified(path + ".txt"))
                meta = OmsiImageryMeta.Parse(System.Text.Encoding.UTF8.GetString(Workspace.ReadAllBytes(path + ".txt")));
            return Workspace.ReadAllBytes(path);
        }

        /// <summary>The project's own pictures (Mapforge\Pictures\pictures.txt), the last added last.</summary>
        public List<OmsiOwnPicture> ReadOwnPictures()
        {
            var workspace = Workspace;
            return workspace != null && workspace.IsModified(OmsiOwnPictures.ListPath)
                ? OmsiOwnPictures.Parse(System.Text.Encoding.UTF8.GetString(workspace.ReadAllBytes(OmsiOwnPictures.ListPath)))
                : new List<OmsiOwnPicture>();
        }

        public void SaveOwnPictures(IEnumerable<OmsiOwnPicture> pictures)
        {
            if (pictures == null)
                throw new ArgumentNullException(nameof(pictures));
            RequireWorkspace().Save(OmsiOwnPictures.ListPath, System.Text.Encoding.UTF8.GetBytes(OmsiOwnPictures.Write(pictures)));
        }

        /// <summary>Keeps the copy of a picture file under Mapforge\Pictures.</summary>
        public void SaveOwnPictureFile(string fileName, byte[] bytes)
        {
            if (string.IsNullOrEmpty(fileName) || fileName.IndexOfAny(new[] { '\\', '/', ' ' }) >= 0)
                throw new ArgumentException("A picture file name has no folders and no spaces.", nameof(fileName));
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            RequireWorkspace().Save(OmsiOwnPictures.Folder + "\\" + fileName, bytes);
        }

        /// <summary>A picture file's copy, or null when the project has none by that name.</summary>
        public byte[] ReadOwnPictureFile(string fileName)
        {
            var path = OmsiOwnPictures.Folder + "\\" + fileName;
            return Workspace != null && Workspace.IsModified(path) ? Workspace.ReadAllBytes(path) : null;
        }

        public void DeleteOwnPictureFile(string fileName)
        {
            var path = OmsiOwnPictures.Folder + "\\" + fileName;
            var workspace = RequireWorkspace();
            if (workspace.IsModified(path))
                workspace.Revert(path);
        }

        /// <summary>Whether an object keeps its height as written rather than above the ground, as junctions do.</summary>
        private static bool HasAbsoluteHeight(string installation, string assetPath)
        {
            try
            {
                var path = OmsiRelativePath.Resolve(installation, assetPath);
                if (!System.IO.File.Exists(path))
                    return false;
                var sceneryObject = OmsiSceneryObject.Parse(System.IO.File.ReadAllBytes(path));
                return sceneryObject.HasAbsoluteHeight || sceneryObject.HasSplineHelper;
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is System.IO.InvalidDataException ||
                                              exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Keeps the stroke as one step: the shaped ground of the touched tiles is saved, and the ground they show is fitted
        /// to the roads again from it, with their neighbours. Null when nothing changed.
        /// </summary>
        public OmsiTerrainChange EndTerrainStroke(OmsiMapSummary summary, OmsiTerrainStroke stroke)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (stroke == null)
                throw new ArgumentNullException(nameof(stroke));
            var workspace = RequireWorkspace();
            var change = new OmsiTerrainChange();
            foreach (var tile in stroke.Shaped.Changed)
            {
                var terrainPath = OmsiRelativePath.Normalize(tile.FileName) + ".terrain";
                var shapedPath = ShapedTerrainPath(terrainPath);
                var before = workspace.IsModified(shapedPath) ? workspace.ReadAllBytes(shapedPath) : null;
                var after = OmsiTerrain.Encode(stroke.Shaped.Loaded[tile]);
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = shapedPath, Before = before, After = after });
                change.Tiles.Add(tile);
            }
            if (change.Files.Count == 0)
                return null;
            // Tiles the Water brush dug in get water at its surface, as openMapper lays it.
            foreach (var tile in stroke.WaterTiles)
                if (tile.Water == null)
                {
                    AddWaterFiles(change, tile, stroke.WaterLevel);
                    if (!change.Tiles.Contains(tile))
                        change.Tiles.Add(tile);
                }
            ApplyFiles(change.Files, false);
            FitTerrainToRoads(summary, change);
            ReadWaterOf(change.Tiles);
            return change;
        }

        /// <summary>
        /// Gives a tile water at a height, all four corners alike, or takes it away (null): the [water] section beside
        /// [terrain] and tile.map.water with one surface. One step, or null when nothing changes.
        /// </summary>
        public OmsiTerrainChange SetTileWater(OmsiMapSummary summary, OmsiTileSummary tile, double? level)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            var change = new OmsiTerrainChange();
            change.Tiles.Add(tile);
            if (level.HasValue)
                AddWaterFiles(change, tile, level.Value);
            else
                RemoveWaterFiles(change, tile);
            if (change.Files.Count == 0)
                return null;
            ApplyFiles(change.Files, false);
            ReadWaterOf(change.Tiles);
            return change;
        }

        /// <summary>The bytes of tile.map.water with one surface at a height at all four corners.</summary>
        internal static byte[] WaterBytes(double level)
        {
            var bytes = new byte[20];
            BitConverter.GetBytes(1u).CopyTo(bytes, 0);
            for (var corner = 0; corner < 4; corner++)
                BitConverter.GetBytes((float)level).CopyTo(bytes, 4 + corner * 4);
            return bytes;
        }

        private void AddWaterFiles(OmsiTerrainChange change, OmsiTileSummary tile, double level)
        {
            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var mapBefore = workspace.ReadAllBytes(tilePath);
            var file = OmsiRecordFile.Parse(mapBefore, OmsiTileRecordLayouts.Instance);
            if (!file.Records.Exists(record => string.Equals(record.Name, "water", StringComparison.OrdinalIgnoreCase)))
            {
                // As OMSI's editor writes it: right after [terrain], an empty line of its own, then the blank line.
                var terrain = file.Records.FindIndex(record => string.Equals(record.Name, "terrain", StringComparison.OrdinalIgnoreCase));
                var version = file.Records.FindIndex(record => string.Equals(record.Name, "version", StringComparison.OrdinalIgnoreCase));
                file.InsertRecord(terrain >= 0 ? terrain + 1 : version + 1, file.CreateRecord("water", new string[0], 2));
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = tilePath, Before = mapBefore, After = file.ToBytes() });
            }
            var waterPath = tilePath + ".water";
            var waterBefore = workspace.FileExists(waterPath) ? workspace.ReadAllBytes(waterPath) : null;
            var waterAfter = WaterBytes(level);
            if (waterBefore == null || !SameBytes(waterBefore, waterAfter))
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = waterPath, Before = waterBefore, After = waterAfter });
        }

        private void RemoveWaterFiles(OmsiTerrainChange change, OmsiTileSummary tile)
        {
            var workspace = RequireWorkspace();
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var mapBefore = workspace.ReadAllBytes(tilePath);
            var file = OmsiRecordFile.Parse(mapBefore, OmsiTileRecordLayouts.Instance);
            var water = file.Records.FindAll(record => string.Equals(record.Name, "water", StringComparison.OrdinalIgnoreCase));
            if (water.Count == 0)
                return;
            foreach (var record in water)
                file.RemoveRecord(record);
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = tilePath, Before = mapBefore, After = file.ToBytes() });
            // Without [water] the game ignores tile.map.water; one the original map has stays, as OMSI's editor leaves it.
            var waterPath = tilePath + ".water";
            if (workspace.FileExists(waterPath) && workspace.ReadOriginalBytes(waterPath) == null)
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = waterPath, Before = workspace.ReadAllBytes(waterPath), After = null });
        }

        /// <summary>Reads the water of tiles again from the workspace.</summary>
        private void ReadWaterOf(IEnumerable<OmsiTileSummary> tiles)
        {
            var workspace = RequireWorkspace();
            foreach (var tile in tiles)
            {
                var tilePath = OmsiRelativePath.Normalize(tile.FileName);
                var file = OmsiRecordFile.Parse(workspace.ReadAllBytes(tilePath), OmsiTileRecordLayouts.Instance);
                tile.Water = file.Records.Exists(record => string.Equals(record.Name, "water", StringComparison.OrdinalIgnoreCase))
                    ? OmsiMapReader.ReadWater(workspace, tilePath)
                    : null;
            }
        }

        /// <summary>Takes back ground shaped by hand: the shaped ground as it was, and the ground shown fitted to the roads again.</summary>
        public void Undo(OmsiMapSummary summary, OmsiTerrainChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (!change.IsApplied)
                throw new InvalidOperationException("The change has already been undone.");
            ApplyFiles(change.Files, true);
            FitTerrainToRoads(summary, change);
            ReadWaterOf(change.Tiles);
            change.IsApplied = false;
        }

        /// <summary>Applies undone ground shaping again.</summary>
        public void Redo(OmsiMapSummary summary, OmsiTerrainChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (change.IsApplied)
                throw new InvalidOperationException("The change is already applied.");
            ApplyFiles(change.Files, false);
            FitTerrainToRoads(summary, change);
            ReadWaterOf(change.Tiles);
            change.IsApplied = true;
        }

        /// <summary>The shown ground of a tile changed by a stroke, fitted to the roads from its shaped ground; the tiles drawn again are added.</summary>
        private void FitTerrainToRoads(OmsiMapSummary summary, OmsiTerrainChange change)
        {
            var refitted = new List<OmsiTileSummary>();
            ReshapeTerrainOf(summary, change.Tiles, refitted);
            foreach (var tile in refitted)
                if (!change.Tiles.Contains(tile))
                    change.Tiles.Add(tile);
        }

        /// <summary>The samples of the shown ground around a place, before a brush step changes them.</summary>
        private static Dictionary<KeyValuePair<long, long>, float> Snapshot(OmsiTerrainGrid grid, double x, double y, double radius)
        {
            var result = new Dictionary<KeyValuePair<long, long>, float>();
            var cell = grid.CellMeters;
            for (var gy = (long)Math.Floor((y - radius) / cell) - 1; gy <= (long)Math.Ceiling((y + radius) / cell) + 1; gy++)
                for (var gx = (long)Math.Floor((x - radius) / cell) - 1; gx <= (long)Math.Ceiling((x + radius) / cell) + 1; gx++)
                {
                    var height = grid.Get(gx, gy);
                    if (height.HasValue)
                        result[new KeyValuePair<long, long>(gx, gy)] = height.Value;
                }
            return result;
        }

        /// <summary>
        /// Gives the shaped ground the change a step made to the shown ground, sample by sample, and writes the shown ground
        /// of the tiles the step touched; returns them.
        /// </summary>
        private List<OmsiTileSummary> Follow(OmsiTerrainStroke stroke, Dictionary<KeyValuePair<long, long>, float> before)
        {
            foreach (var pair in before)
            {
                var now = stroke.Shown.Get(pair.Key.Key, pair.Key.Value);
                if (!now.HasValue || Math.Abs(now.Value - pair.Value) < 1e-6)
                    continue;
                var shaped = stroke.Shaped.Get(pair.Key.Key, pair.Key.Value);
                if (shaped.HasValue)
                    stroke.Shaped.Set(pair.Key.Key, pair.Key.Value, shaped.Value + (now.Value - pair.Value));
            }
            var workspace = RequireWorkspace();
            var result = new List<OmsiTileSummary>();
            foreach (var tile in stroke.Shown.Changed)
            {
                workspace.Save(OmsiRelativePath.Normalize(tile.FileName) + ".terrain", OmsiTerrain.Encode(stroke.Shown.Loaded[tile]));
                result.Add(tile);
            }
            stroke.Shown.Changed.Clear();
            return result;
        }
    }
}
