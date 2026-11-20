using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Where the inspector floats next to the element it shows (M5 step 3, draft): right of the element, or left when the right side is too
    /// narrow, or below/above when neither fits, and always inside the free area so it never reaches under the bottom bar or off the screen.
    /// </summary>
    public static class InspectorPlacement
    {
        /// <summary>The distance from the anchor that stays free so the window does not cover the element, in pixels.</summary>
        public const float Clearance = 28f;

        /// <summary>The distance the window keeps from the edges of the free area, in pixels.</summary>
        public const float Margin = 8f;

        /// <summary>What the window hangs at (owner decision, 20 November 2026).</summary>
        public enum Anchor
        {
            /// <summary>Nothing: the window docks at the right edge.</summary>
            None,
            /// <summary>One or several objects: their middle.</summary>
            Objects,
            /// <summary>One or several splines: their middle.</summary>
            Spline,
            /// <summary>The selected row of objects along a road: the object that was clicked, or the first.</summary>
            Row,
            /// <summary>A tile chosen on its own: the middle of the tile.</summary>
            Tile,
        }

        /// <summary>Which anchor the selection gives, in the order objects, spline, row, tile; none with nothing chosen at all.</summary>
        public static Anchor ChooseAnchor(int objectCount, bool splineSelected, bool rowSelected, bool tileChosen)
        {
            if (objectCount > 0)
                return Anchor.Objects;
            if (splineSelected)
                return Anchor.Spline;
            if (rowSelected)
                return Anchor.Row;
            return tileChosen ? Anchor.Tile : Anchor.None;
        }

        /// <summary>The top left corner of a window of <paramref name="size"/> beside <paramref name="anchor"/>, all in the coordinates of <paramref name="area"/>.</summary>
        public static Vector2 Place(Vector2 anchor, Vector2 size, Rect area, float clearance, float margin)
        {
            var inner = Rect.MinMaxRect(area.xMin + margin, area.yMin + margin, Mathf.Max(area.xMin + margin, area.xMax - margin), Mathf.Max(area.yMin + margin, area.yMax - margin));
            var ax = Mathf.Clamp(anchor.x, inner.xMin, inner.xMax);
            var ay = Mathf.Clamp(anchor.y, inner.yMin, inner.yMax);
            float x;
            float y;
            if (ax + clearance + size.x <= inner.xMax)
            {
                x = ax + clearance;
                y = ay - size.y * 0.25f;
            }
            else if (ax - clearance - size.x >= inner.xMin)
            {
                x = ax - clearance - size.x;
                y = ay - size.y * 0.25f;
            }
            else
            {
                // Too narrow at both sides: above or below, centred on the element.
                x = ax - size.x / 2f;
                y = ay + clearance + size.y <= inner.yMax ? ay + clearance : ay - clearance - size.y;
            }
            return new Vector2(
                Mathf.Clamp(x, inner.xMin, Mathf.Max(inner.xMin, inner.xMax - size.x)),
                Mathf.Clamp(y, inner.yMin, Mathf.Max(inner.yMin, inner.yMax - size.y)));
        }

        /// <summary>
        /// The top left corner of a window of <paramref name="size"/> beside the screen rectangle <paramref name="extent"/> of the element (a tile's four
        /// corners, the outline of several objects, the centre line of a spline): right of it with <paramref name="clearance"/> between, else left, above,
        /// below, always inside the free area. When the element is larger than any side leaves room for (zoomed in until it fills the view) the window
        /// hangs at <paramref name="anchor"/> exactly as <see cref="Place(Vector2, Vector2, Rect, float, float)"/> does. All in the coordinates of <paramref name="area"/>.
        /// </summary>
        public static Vector2 Place(Rect extent, Vector2 anchor, Vector2 size, Rect area, float clearance, float margin)
        {
            var inner = Rect.MinMaxRect(area.xMin + margin, area.yMin + margin, Mathf.Max(area.xMin + margin, area.xMax - margin), Mathf.Max(area.yMin + margin, area.yMax - margin));
            var ay = Mathf.Clamp(anchor.y, inner.yMin, inner.yMax);
            var ax = Mathf.Clamp(anchor.x, inner.xMin, inner.xMax);
            var fitsHeight = size.y <= inner.height;
            var fitsWidth = size.x <= inner.width;
            if (fitsHeight && extent.xMax + clearance + size.x <= inner.xMax && extent.xMax + clearance >= inner.xMin)
                return new Vector2(extent.xMax + clearance, Mathf.Clamp(ay - size.y * 0.25f, inner.yMin, inner.yMax - size.y));
            if (fitsHeight && extent.xMin - clearance - size.x >= inner.xMin && extent.xMin - clearance <= inner.xMax)
                return new Vector2(extent.xMin - clearance - size.x, Mathf.Clamp(ay - size.y * 0.25f, inner.yMin, inner.yMax - size.y));
            if (fitsWidth && extent.yMin - clearance - size.y >= inner.yMin && extent.yMin - clearance <= inner.yMax)
                return new Vector2(Mathf.Clamp(ax - size.x / 2f, inner.xMin, inner.xMax - size.x), extent.yMin - clearance - size.y);
            if (fitsWidth && extent.yMax + clearance + size.y <= inner.yMax && extent.yMax + clearance >= inner.yMin)
                return new Vector2(Mathf.Clamp(ax - size.x / 2f, inner.xMin, inner.xMax - size.x), extent.yMax + clearance);
            return Place(anchor, size, area, clearance, margin);
        }

        /// <summary>The smallest rectangle holding all points; false without any point.</summary>
        public static bool TryBounds(System.Collections.Generic.IReadOnlyList<Vector2> points, out Rect bounds)
        {
            bounds = default;
            if (points == null || points.Count == 0)
                return false;
            var min = points[0];
            var max = points[0];
            for (var index = 1; index < points.Count; index++)
            {
                min = Vector2.Min(min, points[index]);
                max = Vector2.Max(max, points[index]);
            }
            bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        /// <summary>
        /// Whether the window is transparent: while the pointer moves something, and for <paramref name="holdSeconds"/> after it, so the window never
        /// switches back and forth between two events of one drag. <paramref name="busyUntil"/> keeps the end of the hold between calls.
        /// </summary>
        public static bool IsBusy(bool pointerMoving, float now, ref float busyUntil, float holdSeconds)
        {
            if (pointerMoving)
                busyUntil = now + holdSeconds;
            return pointerMoving || now < busyUntil;
        }

        /// <summary>The point of <paramref name="rect"/> nearest to <paramref name="point"/>: where the line from the window to the element starts.</summary>
        public static Vector2 NearestPoint(Rect rect, Vector2 point)
        {
            return new Vector2(Mathf.Clamp(point.x, rect.xMin, rect.xMax), Mathf.Clamp(point.y, rect.yMin, rect.yMax));
        }
    }
}
