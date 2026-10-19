using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>Draws the object positions and precomputed approximate spline paths inside one map tile.</summary>
    internal sealed class MapTileVisualElement : VisualElement
    {
        private const float Padding = 8f;
        private static readonly Color SplineColor = new Color(0.95f, 0.71f, 0.30f, 0.95f);
        private static readonly Color ObjectColor = new Color(0.56f, 0.82f, 0.94f, 0.95f);
        private readonly OmsiTileSummary tile;
        private readonly IReadOnlyList<OmsiLineSegment> splineSegments;
        private readonly double tileSize;

        public MapTileVisualElement(OmsiTileSummary tile, IReadOnlyList<OmsiLineSegment> splineSegments, double tileSize,
            bool showDetails, bool isSelected,
            Action<OmsiTileSummary> onSelected)
        {
            this.tile = tile;
            this.splineSegments = splineSegments ?? Array.Empty<OmsiLineSegment>();
            this.tileSize = tileSize;
            AddToClassList("map-tile");
            pickingMode = PickingMode.Position;
            focusable = true;
            SetSelected(isSelected);
            if (!tile.IsAvailable)
                AddToClassList("map-tile-missing");

            if (onSelected != null)
            {
                RegisterCallback<ClickEvent>(_ => onSelected(tile));
                RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space)
                        return;
                    onSelected(tile);
                    evt.StopPropagation();
                });
            }

            var title = new Label("Tile " + tile.X + ", " + tile.Y);
            title.AddToClassList("map-tile-title");
            title.tooltip = tile.FileName;
            Add(title);

            if (showDetails)
            {
                var recordCounts = new Label("Objects " + tile.ObjectCount + "  |  Splines " + tile.SplineCount);
                recordCounts.AddToClassList("map-tile-counts");
                Add(recordCounts);

                var terrain = new Label(tile.HasTerrain
                    ? "Terrain " + tile.TerrainMinHeight.ToString("F1") + " .. " + tile.TerrainMaxHeight.ToString("F1") + " m"
                    : "Terrain unavailable");
                terrain.AddToClassList("map-tile-terrain");
                Add(terrain);
            }

            generateVisualContent += DrawMapRecords;
        }

        /// <summary>Updates the selection highlight without rebuilding the tile.</summary>
        public void SetSelected(bool isSelected)
        {
            EnableInClassList("map-tile-selected", isSelected);
        }

        private void DrawMapRecords(MeshGenerationContext context)
        {
            if (tileSize <= 0.0)
                return;

            var painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            painter.strokeColor = new Color(0.24f, 0.30f, 0.35f, 0.75f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            for (var division = 1; division < 4; division++)
            {
                var offset = Padding + (resolvedStyle.width - Padding * 2f) * division / 4f;
                painter.MoveTo(new Vector2(offset, Padding));
                painter.LineTo(new Vector2(offset, resolvedStyle.height - Padding));
                painter.MoveTo(new Vector2(Padding, offset));
                painter.LineTo(new Vector2(resolvedStyle.width - Padding, offset));
            }
            painter.Stroke();

            painter.strokeColor = SplineColor;
            painter.lineWidth = 2.4f;
            if (splineSegments.Count > 0)
            {
                painter.BeginPath();
                foreach (var segment in splineSegments)
                {
                    painter.MoveTo(ToLocalPoint(segment.Start.X, segment.Start.Y, resolvedStyle.width, resolvedStyle.height));
                    painter.LineTo(ToLocalPoint(segment.End.X, segment.End.Y, resolvedStyle.width, resolvedStyle.height));
                }
                painter.Stroke();
            }

            painter.fillColor = ObjectColor;
            foreach (var item in tile.Objects)
            {
                var point = ToLocalPoint(item.LocalX, item.LocalY, resolvedStyle.width, resolvedStyle.height);
                if (point.x < Padding || point.x > resolvedStyle.width - Padding ||
                    point.y < Padding || point.y > resolvedStyle.height - Padding)
                    continue;

                painter.BeginPath();
                painter.Arc(point, 2.2f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Fill();
            }
        }

        private Vector2 ToLocalPoint(double x, double y, float width, float height)
        {
            var scaleX = (width - Padding * 2f) / (float)tileSize;
            var scaleY = (height - Padding * 2f) / (float)tileSize;
            return new Vector2(Padding + (float)x * scaleX,
                Padding + (float)(tileSize - y) * scaleY);
        }
    }
}
