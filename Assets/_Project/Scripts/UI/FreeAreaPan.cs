using System.Collections.Generic;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Where an object has to stand on the screen to be seen above a tool window (ADR 098, M4): in the part of the map between the top of the
    /// map and the window, not too close to either edge. Pure, so it can be tested.
    /// </summary>
    internal static class FreeAreaPan
    {
        /// <summary>How close to the window or the top edge an object may stand before the view moves, in pixels.</summary>
        public const float MarginPixels = 90f;

        /// <summary>
        /// How far the view has to move for a point at (<paramref name="pointX"/>, <paramref name="pointY"/>) to be seen above the tool windows
        /// standing at <paramref name="windows"/> (left, top, right, bottom each, in panel pixels; the map starts at <paramref name="mapTop"/>).
        /// Only a window the point is under, or within the margin of sideways or above, counts; the free part is then the map above the highest
        /// such window. Nothing when no window is near the point.
        /// </summary>
        public static float ShiftFor(float pointX, float pointY, IReadOnlyList<UnityEngine.Rect> windows, float mapTop, float margin)
        {
            if (windows == null || float.IsNaN(pointX) || float.IsNaN(pointY))
                return 0f;
            var blockingTop = float.NaN;
            foreach (var window in windows)
            {
                if (window.width < 1f || window.height < 1f || float.IsNaN(window.yMin))
                    continue;
                var near = pointX >= window.xMin - margin && pointX <= window.xMax + margin && pointY > window.yMin - margin;
                if (near && (float.IsNaN(blockingTop) || window.yMin < blockingTop))
                    blockingTop = window.yMin;
            }
            return float.IsNaN(blockingTop) ? 0f : ShiftY(pointY, mapTop, blockingTop, margin);
        }

        /// <summary>The top edge of the highest window (the bottom of the free part of the map), NaN when none of them has its place yet.</summary>
        public static float FreeBottom(IReadOnlyList<UnityEngine.Rect> windows)
        {
            var top = float.NaN;
            if (windows == null)
                return top;
            foreach (var window in windows)
                if (window.width >= 1f && window.height >= 1f && !float.IsNaN(window.yMin) && (float.IsNaN(top) || window.yMin < top))
                    top = window.yMin;
            return top;
        }

        /// <summary>
        /// How far an object at <paramref name="pointY"/> has to move down (positive) or up (negative) on the screen: nothing while it stands
        /// within the free part (<paramref name="freeTop"/> to <paramref name="freeBottom"/>) minus the margin at both edges, else to the
        /// middle of the free part. Nothing when the free part is too small to keep anything clear.
        /// </summary>
        public static float ShiftY(float pointY, float freeTop, float freeBottom, float margin)
        {
            if (float.IsNaN(pointY) || float.IsNaN(freeTop) || float.IsNaN(freeBottom) || freeBottom - freeTop <= margin * 2f)
                return 0f;
            if (pointY >= freeTop + margin && pointY <= freeBottom - margin)
                return 0f;
            return (freeTop + freeBottom) * 0.5f - pointY;
        }
    }

    /// <summary>What a pending request to keep a target clear of a tool window does on its next look.</summary>
    internal enum FreeAreaStep
    {
        /// <summary>Look again in a moment (the pointer holds something, or the window has no place yet).</summary>
        Wait,

        /// <summary>Forget the request without moving the view.</summary>
        Drop,

        /// <summary>Move the view now.</summary>
        Pan
    }

    /// <summary>
    /// The decisions of a pending request (ADR 098, M4), apart from the window and the map so they can be tested: a newer request, a left
    /// tool, a changed view or a target the user moved in the meantime drop it; while the pointer holds something it waits.
    /// </summary>
    internal static class FreeAreaRequest
    {
        /// <summary>A target moved by more than this since the request (metres) was moved on purpose.</summary>
        public const double MovedMeters = 0.01;

        /// <param name="superseded">Another request came, the map is gone or the tool was left.</param>
        /// <param name="dragging">The pointer holds something on the map.</param>
        /// <param name="viewChanged">The user panned or zoomed since the request.</param>
        /// <param name="targetMoved">The target stands elsewhere than when it was requested (it was dragged).</param>
        /// <param name="windowPlaced">The tool window has a place already.</param>
        public static FreeAreaStep Next(bool superseded, bool dragging, bool viewChanged, bool targetMoved, bool windowPlaced)
        {
            if (superseded)
                return FreeAreaStep.Drop;
            // Held by the pointer (a click on an object starts a drag): nothing slips away under it; once let go, a target that was moved stays where the user put it.
            if (dragging)
                return FreeAreaStep.Wait;
            if (viewChanged || targetMoved)
                return FreeAreaStep.Drop;
            return windowPlaced ? FreeAreaStep.Pan : FreeAreaStep.Wait;
        }

        /// <summary>True when the target stands elsewhere now than at the request; a target missing at either time is not moved.</summary>
        public static bool Moved(double requestedX, double requestedY, double nowX, double nowY)
        {
            return System.Math.Abs(requestedX - nowX) > MovedMeters || System.Math.Abs(requestedY - nowY) > MovedMeters;
        }
    }
}
