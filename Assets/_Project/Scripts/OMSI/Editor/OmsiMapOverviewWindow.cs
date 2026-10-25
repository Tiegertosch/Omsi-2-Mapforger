using System;
using System.Linq;
using OmsiMapEditor.OMSI;
using UnityEditor;
using UnityEngine;

namespace OmsiMapEditor.OMSI.Editor
{
    /// <summary>Shows a read-only tile and record overview for an OMSI map.</summary>
    public sealed class OmsiMapOverviewWindow : EditorWindow
    {
        private const float TileWidth = 112f;
        private const float TileHeight = 112f;
        private const float TileGap = 8f;

        private OmsiMapSummary summary;
        private OmsiSplineTileIndex splineIndex;
        private OmsiSplineContinuityReport continuity;
        private Vector2 mapScroll;

        [MenuItem("OMSI MapWorks/Import/Read OMSI Map...")]
        private static void OpenWindow()
        {
            var window = GetWindow<OmsiMapOverviewWindow>();
            window.titleContent = new GUIContent("OMSI Map");
            window.minSize = new Vector2(520f, 360f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Open global.cfg...", EditorStyles.toolbarButton, GUILayout.Width(140f)))
                OpenMap();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Read-only overview", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (summary == null)
            {
                EditorGUILayout.HelpBox("Choose an OMSI map's global.cfg to inspect its tile layout and record counts. Source files are read without being copied or changed.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(summary.FriendlyName) ? summary.Name : summary.FriendlyName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Map: " + summary.Name + "    |    Tiles: " + summary.Tiles.Count +
                                      "    |    Objects: " + summary.ObjectCount +
                                      "    |    Splines: " + summary.SplineCount +
                                      "    |    Attachments: " + summary.AttachmentCount +
                                      "    |    Terrain: " + summary.TerrainTileCount + "/" + summary.Tiles.Count +
                                      (summary.UsesWorldCoordinates
                                          ? "    |    World coordinates: " + summary.TileSizeMeters.ToString("0.0") + " m tiles"
                                          : string.Empty),
                EditorStyles.wordWrappedMiniLabel);
            if (continuity != null)
                EditorGUILayout.LabelField(DescribeContinuity(continuity), EditorStyles.wordWrappedMiniLabel);

            if (!string.IsNullOrWhiteSpace(summary.Description))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(summary.Description, EditorStyles.wordWrappedLabel);
            }

            DrawTileMap();

            foreach (var warning in summary.Warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);

            EditorGUILayout.LabelField(summary.RootPath, EditorStyles.miniLabel);
        }

        private void OpenMap()
        {
            var path = EditorUtility.OpenFilePanel("Select an OMSI map configuration", string.Empty, "cfg");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                summary = OmsiMapReader.Read(path);
                splineIndex = OmsiSplineTileIndex.Build(summary);
                continuity = OmsiSplineContinuity.Check(summary);
                OmsiContentBoundary.RememberSourcePath(summary.RootPath);
                titleContent = new GUIContent(string.IsNullOrEmpty(summary.Name) ? "OMSI Map" : summary.Name);
            }
            catch (Exception exception)
            {
                summary = null;
                splineIndex = null;
                continuity = null;
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Could not read OMSI map", exception.Message, "OK");
            }
        }

        private static string DescribeContinuity(OmsiSplineContinuityReport report)
        {
            var text = "Spline links: " + report.ConnectedCount + " of " + report.LinkCount + " meet within " +
                       report.ToleranceMeters.ToString("0.0#") + " m";
            if (report.Gaps.Count > 0)
            {
                var worstGap = report.Gaps[0];
                text += "    |    Largest gap: " + worstGap.DistanceMeters.ToString("0.00") + " m between splines " +
                        worstGap.SplineId + " and " + worstGap.NeighbourId + " in " + worstGap.Tile.FileName +
                        (worstGap.IsMutual ? string.Empty : " (referenced from one side only)");
            }
            if (report.MissingNeighbourCount > 0)
                text += "    |    Links to splines that were not loaded: " + report.MissingNeighbourCount;
            return text;
        }

        private void DrawTileMap()
        {
            if (summary.Tiles.Count == 0)
                return;

            var minX = summary.Tiles.Min(tile => tile.X);
            var maxX = summary.Tiles.Max(tile => tile.X);
            var minY = summary.Tiles.Min(tile => tile.Y);
            var maxY = summary.Tiles.Max(tile => tile.Y);
            var width = (maxX - minX + 1) * (TileWidth + TileGap) + TileGap;
            var height = (maxY - minY + 1) * (TileHeight + TileGap) + TileGap;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Map tiles", EditorStyles.boldLabel);
            mapScroll = EditorGUILayout.BeginScrollView(mapScroll, GUILayout.Height(Mathf.Min(height + 8f, 360f)));
            var canvas = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            EditorGUI.DrawRect(canvas, new Color(0.12f, 0.14f, 0.17f));

            foreach (var tile in summary.Tiles)
            {
                var column = tile.X - minX;
                var row = maxY - tile.Y;
                var rect = new Rect(canvas.x + TileGap + column * (TileWidth + TileGap),
                    canvas.y + TileGap + row * (TileHeight + TileGap), TileWidth, TileHeight);
                var color = tile.IsAvailable
                    ? new Color(0.20f, 0.29f, 0.36f)
                    : new Color(0.42f, 0.18f, 0.18f);
                EditorGUI.DrawRect(rect, color);
                GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
                GUI.Label(new Rect(rect.x + 5f, rect.y + 3f, rect.width - 10f, 16f),
                    "Tile " + tile.X + ", " + tile.Y, EditorStyles.boldLabel);
                GUI.Label(new Rect(rect.x + 5f, rect.y + 18f, rect.width - 10f, 14f),
                    "Obj " + tile.ObjectCount + "  Spl " + tile.SplineCount, EditorStyles.miniLabel);
                GUI.Label(new Rect(rect.x + 5f, rect.y + 31f, rect.width - 10f, 14f),
                    tile.HasTerrain
                        ? "H " + tile.TerrainMinHeight.ToString("F1") + " .. " + tile.TerrainMaxHeight.ToString("F1") + " m"
                        : "Terrain unavailable",
                    EditorStyles.miniLabel);
                DrawTileRecords(tile, splineIndex, rect, summary.TileSizeMeters);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawTileRecords(OmsiTileSummary tile, OmsiSplineTileIndex splineIndex, Rect rect, double tileSize)
        {
            var padding = 7f;
            var scale = (rect.width - padding * 2f) / (float)tileSize;
            Handles.BeginGUI();
            Handles.color = new Color(0.95f, 0.71f, 0.30f, 0.9f);
            if (splineIndex != null)
                foreach (var segment in splineIndex.GetSegments(tile.X, tile.Y))
                    Handles.DrawAAPolyLine(1.6f,
                        ToScreenPoint(segment.Start.X, segment.Start.Y, rect, padding, scale, tileSize),
                        ToScreenPoint(segment.End.X, segment.End.Y, rect, padding, scale, tileSize));

            Handles.color = new Color(0.66f, 0.84f, 0.93f, 0.95f);
            foreach (var item in tile.Objects)
            {
                var point = ToScreenPoint(item.LocalX, item.LocalY, rect, padding, scale, tileSize);
                EditorGUI.DrawRect(new Rect(point.x - 1.4f, point.y - 1.4f, 2.8f, 2.8f), Handles.color);
            }
            Handles.EndGUI();
        }

        private static Vector3 ToScreenPoint(double x, double y, Rect rect, float padding, float scale, double tileSize)
        {
            return new Vector3(rect.x + padding + (float)x * scale,
                rect.y + padding + (float)(tileSize - y) * scale, 0f);
        }
    }
}
