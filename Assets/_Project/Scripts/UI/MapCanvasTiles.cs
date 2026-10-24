using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Adding tiles from the map view (ADR 031): a round plus in the middle of every free place beside a tile, faint until the
    /// pointer is over it, from above and in 3D. A click reports the place; the shell adds the tile.
    /// </summary>
    internal sealed partial class MapCanvasElement
    {
        /// <summary>Tiles smaller than this on screen show no plus, so a zoomed-out large map is not covered in them.</summary>
        private const float AdderMinimumTilePixels = 90f;

        private const float AdderRadius3D = 13f;

        private Vector2Int? hoveredAdder;

        /// <summary>The plus of a free place beside a tile was clicked; the arguments are the new tile's grid numbers.</summary>
        public event Action<int, int> TileAddRequested;

        /// <summary>Pluses show with editing allowed in the Drag tool, while nothing is placed or built.</summary>
        private bool ShowsTileAdders => AllowEditing && tool == EditorTool.Drag && placementAsset == null && !roadMode && summary != null;

        /// <summary>The free places of the grid beside a tile, left, right, below or above it.</summary>
        private List<Vector2Int> FreeTilePlaces()
        {
            var taken = new HashSet<Vector2Int>();
            foreach (var tile in summary.Tiles)
                taken.Add(new Vector2Int(tile.X, tile.Y));
            var free = new List<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            foreach (var tile in summary.Tiles)
                foreach (var step in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up })
                {
                    var place = new Vector2Int(tile.X, tile.Y) + step;
                    if (!taken.Contains(place) && seen.Add(place))
                        free.Add(place);
                }
            return free;
        }

        private Rect PlaceRect(Vector2Int place)
        {
            return new Rect((float)(place.x * tileSize - originX), (float)(place.y * tileSize - originY), (float)tileSize, (float)tileSize);
        }

        /// <summary>Where the plus of a place shows on screen and how large, or false when it does not show.</summary>
        private bool AdderOnScreen(Vector2Int place, out Vector2 centre, out float radius)
        {
            centre = default;
            radius = 0f;
            var rect = PlaceRect(place);
            if (ThreeDimensional)
            {
                // On the ground of the tile beside it, as the free place has none.
                var camera = OrbitCamera();
                var ground = OnGround(NearestTilePoint(place, rect.center), 0f);
                ground.x = rect.center.x;
                ground.z = rect.center.y;
                if (!camera.TryProject(ground, out centre))
                    return false;
                var edge = ground + new Vector3((float)tileSize * 0.5f, 0f, 0f);
                if (!camera.TryProject(edge, out var edgeScreen) || (edgeScreen - centre).magnitude * 2f < AdderMinimumTilePixels)
                    return false;
                radius = AdderRadius3D;
                return centre.x > -radius && centre.y > -radius && centre.x < layout.width + radius && centre.y < layout.height + radius;
            }
            var screenSize = (float)tileSize / metersPerPixel;
            if (screenSize < AdderMinimumTilePixels)
                return false;
            centre = MapToScreen(rect.center);
            radius = Mathf.Clamp(screenSize * 0.04f, 9f, 17f);
            return centre.x > -radius && centre.y > -radius && centre.x < layout.width + radius && centre.y < layout.height + radius;
        }

        /// <summary>A point of a neighbouring tile next to a free place, for the height of the ground there.</summary>
        private Vector2 NearestTilePoint(Vector2Int place, Vector2 fallback)
        {
            foreach (var step in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up })
            {
                var neighbour = place + step;
                if (!summary.Tiles.Exists(tile => tile.X == neighbour.x && tile.Y == neighbour.y))
                    continue;
                var rect = PlaceRect(neighbour);
                return new Vector2(Mathf.Clamp(fallback.x, rect.xMin + 1f, rect.xMax - 1f), Mathf.Clamp(fallback.y, rect.yMin + 1f, rect.yMax - 1f));
            }
            return fallback;
        }

        /// <summary>The free place whose plus lies under a screen position, or null.</summary>
        private Vector2Int? AdderAt(Vector2 position)
        {
            if (!ShowsTileAdders || !hasView)
                return null;
            foreach (var place in FreeTilePlaces())
                if (AdderOnScreen(place, out var centre, out var radius) && (position - centre).sqrMagnitude <= radius * radius)
                    return place;
            return null;
        }

        /// <summary>Remembers the plus under the pointer, so it shows solid.</summary>
        private void UpdateHoveredAdder(Vector2 position)
        {
            var place = AdderAt(position);
            if (place == hoveredAdder)
                return;
            hoveredAdder = place;
            MarkDirtyRepaint();
        }

        private void ClearHoveredAdder()
        {
            if (!hoveredAdder.HasValue)
                return;
            hoveredAdder = null;
            MarkDirtyRepaint();
        }

        /// <summary>Shows the plus of a place as under the pointer, for screenshots from the command line.</summary>
        public void HoverTileAdder(int x, int y)
        {
            hoveredAdder = new Vector2Int(x, y);
            MarkDirtyRepaint();
        }

        /// <summary>Reports a click on a plus; true when there was one under the pointer.</summary>
        private bool ClickAdder(Vector2 position)
        {
            var place = AdderAt(position);
            if (!place.HasValue)
                return false;
            hoveredAdder = null;
            TileAddRequested?.Invoke(place.Value.x, place.Value.y);
            return true;
        }

        /// <summary>Faint round pluses on the free places beside the tiles; the one under the pointer solid, ringed in the accent.</summary>
        private void DrawTileAdders(Painter2D painter)
        {
            if (!ShowsTileAdders || !hasView)
                return;
            foreach (var place in FreeTilePlaces())
            {
                if (!AdderOnScreen(place, out var centre, out var radius))
                    continue;
                var hovered = place == hoveredAdder;
                var background = new Color(0.07f, 0.09f, 0.12f, hovered ? 0.92f : 0.35f);
                var foreground = hovered ? placementColor : new Color(1f, 1f, 1f, 0.55f);
                painter.fillColor = background;
                painter.BeginPath();
                painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Fill();
                painter.strokeColor = foreground;
                painter.lineWidth = hovered ? 2f : 1.5f;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.Arc(centre, radius - 1f, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Stroke();
                var arm = radius * 0.5f;
                painter.BeginPath();
                painter.MoveTo(centre + new Vector2(-arm, 0f));
                painter.LineTo(centre + new Vector2(arm, 0f));
                painter.MoveTo(centre + new Vector2(0f, -arm));
                painter.LineTo(centre + new Vector2(0f, arm));
                painter.Stroke();
            }
        }
    }
}
