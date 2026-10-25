using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The ground shaped by hand: the shaped ground of every tile it touched before and after, so the step can be taken
    /// back and done again; the ground fitted to roads follows from it.
    /// </summary>
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
            OmsiTerrainBrush.Stroke(stroke.Shown, mode, x, y, radius, strength, seconds, stroke.FlattenTo, roads);
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
            ApplyFiles(change.Files, false);
            FitTerrainToRoads(summary, change);
            return change;
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
