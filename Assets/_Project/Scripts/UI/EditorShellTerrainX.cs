using System;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The terrain of a tile as a DirectX mesh (ADR 081): a button in the tile tab of the inspector writes the ground as the map shows it to a
    /// .x file, for a 3D program.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The button under the tile's facts; not for a tile without terrain.</summary>
        private void AddTerrainExportButton(VisualElement container, OmsiTileSummary tile)
        {
            if (tile == null || !tile.IsAvailable || !tile.HasTerrain)
                return;
            var button = new Button(() => ExportTerrainX(tile, null));
            LocBindings.Text(button, "terrainx.export");
            LocBindings.Tooltip(button, "terrainx.export.tooltip");
            button.AddToClassList("map-control-button");
            button.AddToClassList("inspector-edit-apply");
            container.Add(button);
        }

        /// <summary>Writes the terrain of a tile as a .x file where the user chooses, or at the path given.</summary>
        private void ExportTerrainX(OmsiTileSummary tile, string path)
        {
            if (currentSummary == null || session == null || tile == null)
                return;
            if (!tile.IsAvailable || !tile.HasTerrain)
            {
                SetStatusT("terrainx.status.none", ("x", tile.X), ("y", tile.Y));
                return;
            }
            var name = string.IsNullOrWhiteSpace(currentSummary.FriendlyName) ? currentSummary.Name : currentSummary.FriendlyName;
            var suggested = SanitizeFileName(name) + "_tile_" + tile.X.ToString(CultureInfo.InvariantCulture) + "_" + tile.Y.ToString(CultureInfo.InvariantCulture) + "_terrain.x";
            path = path ?? WindowsFileDialog.SaveFile(Loc.T("filedialog.export_terrain"), WindowsFileDialog.DirectXFilter, "x", suggested);
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                var export = session.ExportTerrainMesh(currentSummary, tile);
                File.WriteAllBytes(path, export.Bytes);
                SetStatusT("terrainx.status.written", ("x", tile.X), ("y", tile.Y), ("file", Path.GetFileName(path)), ("vertices", export.Vertices), ("triangles", export.Triangles),
                    ("lowest", export.Lowest), ("highest", export.Highest));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException ||
                                              exception is InvalidDataException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("terrainx.status.failed", ("reason", exception.Message));
            }
        }

        /// <summary>Command line: <c>-terrainExportX tileX tileY file.x</c> writes the terrain of that tile; "Terrain X:" in the log. ADR 081.</summary>
        private void RunTerrainXArguments(string[] args)
        {
            var index = Array.IndexOf(args, "-terrainExportX");
            if (index < 0 || index + 3 >= args.Length || session == null || currentSummary == null ||
                !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileX) ||
                !int.TryParse(args[index + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tileY))
                return;
            var tile = currentSummary.Tiles.Find(candidate => candidate.X == tileX && candidate.Y == tileY);
            if (tile == null)
            {
                Debug.Log("Terrain X: no tile " + tileX + ", " + tileY);
                return;
            }
            ExportTerrainX(tile, args[index + 3]);
            Debug.Log("Terrain X: " + statusLabel?.text);
        }
    }
}
