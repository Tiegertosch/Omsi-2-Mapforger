using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// What the floating windows beside the map share (the window for pictures from the air and terrain, the signals window): dragging by a
    /// handle, staying inside the area around them when that gets smaller, and resizing by a grip. The rules are in
    /// <see cref="FloatingWindowRules"/>. A window keeps where it was put for as long as it is not destroyed, so closing and opening it again
    /// finds it in the same place.
    /// </summary>
    internal static class FloatingWindow
    {
        /// <summary>
        /// What a window keeps to besides staying inside its area: a least size, the margin to the area's edges and the room the bottom bar takes
        /// (<see cref="FloatingWindowRules.Fit"/>). The satellite window has none and only keeps its place.
        /// </summary>
        public sealed class Limits
        {
            public Vector2 Least;
            public float Margin;
            public float BottomInset;

            /// <summary>The height the window is meant to have when it is not its displayed one (a collapsed window is as high as its title bar); null for the displayed height.</summary>
            public Func<float> Height;

            /// <summary>Called with the height the window keeps when that is not what its style says (collapsed windows); null for none.</summary>
            public Action<float> HeightFitted;

            /// <summary>Whether the window is collapsed: its height style is then left alone.</summary>
            public Func<bool> Collapsed;
        }

        /// <summary>
        /// Lets the window be dragged by <paramref name="handle"/> (not by a button in it) and keeps it inside its parent, also when the
        /// parent or the window itself changes its size.
        /// </summary>
        public static void MakeMovable(VisualElement window, VisualElement handle, Action moved = null, Limits limits = null)
        {
            Vector2? grabbed = null;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.target is Button || (evt.target as VisualElement)?.GetFirstAncestorOfType<Button>() != null)
                    return;
                grabbed = (Vector2)evt.position - new Vector2(window.layout.x, window.layout.y);
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!grabbed.HasValue || !handle.HasPointerCapture(evt.pointerId))
                    return;
                if (MoveTo(window, (Vector2)evt.position - grabbed.Value, limits))
                    moved?.Invoke();
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!handle.HasPointerCapture(evt.pointerId))
                    return;
                handle.ReleasePointer(evt.pointerId);
                grabbed = null;
                evt.StopPropagation();
            });
            EventCallback<GeometryChangedEvent> keepInside = _ =>
            {
                if (window.resolvedStyle.display == DisplayStyle.None)
                    return;
                if (limits != null)
                    FitSize(window, limits);
                MoveTo(window, new Vector2(window.layout.x, window.layout.y), limits);
            };
            // A smaller area around it, or a window that grows (another tab, the grip), keeps the window reachable.
            window.RegisterCallback<AttachToPanelEvent>(_ => window.parent?.RegisterCallback(keepInside));
            window.RegisterCallback<DetachFromPanelEvent>(_ => window.parent?.UnregisterCallback(keepInside));
            window.RegisterCallback(keepInside);
        }

        /// <summary>Puts the window at the position, clamped into its parent; writes the style only when the place changes. True when it did.</summary>
        public static bool MoveTo(VisualElement window, Vector2 position, Limits limits = null)
        {
            var parent = window.parent;
            if (parent == null)
                return false;
            var area = parent.contentRect;
            if (area.width < 1f || area.height < 1f)
                return false;
            var bottom = limits != null ? limits.BottomInset : 0f;
            var place = FloatingWindowRules.Clamp(position, new Vector2(window.layout.width, window.layout.height), new Vector2(area.width, Mathf.Max(0f, area.height - bottom)));
            var changed = false;
            if (!Same(window.style.left, place.x))
            {
                window.style.left = place.x;
                changed = true;
            }
            if (!Same(window.style.top, place.y))
            {
                window.style.top = place.y;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// A smaller area around the window makes the window smaller (not below its least, which gives way to an area smaller than it). Writes
        /// the style only when the size changes, as the window's own size change comes back here.
        /// </summary>
        private static void FitSize(VisualElement window, Limits limits)
        {
            var parent = window.parent;
            if (parent == null)
                return;
            var area = parent.contentRect;
            if (area.width < 1f || area.height < 1f || window.layout.width < 1f)
                return;
            var collapsed = limits.Collapsed != null && limits.Collapsed();
            var height = limits.Height != null ? limits.Height() : window.layout.height;
            // A window collapsed before it was first placed has no height of its own yet.
            if (height < 1f)
                return;
            var fit = FloatingWindowRules.Fit(new Vector2(window.layout.x, window.layout.y), new Vector2(window.layout.width, height), collapsed ? window.layout.height : height,
                limits.Least, new Vector2(area.width, area.height), limits.Margin, limits.BottomInset);
            if (Mathf.Abs(fit.Size.x - window.layout.width) > 0.5f)
                window.style.width = fit.Size.x; // computed: window size from fit and saved geometry
            if (Mathf.Abs(fit.Size.y - height) > 0.5f)
            {
                if (!collapsed)
                    window.style.height = fit.Size.y; // computed: window size from fit and saved geometry
                limits.HeightFitted?.Invoke(fit.Size.y);
            }
        }

        private static bool Same(StyleLength current, float value)
        {
            return current.keyword == StyleKeyword.Undefined && Mathf.Abs(current.value.value - value) < 0.5f;
        }

        /// <summary>Lets the window be resized by <paramref name="grip"/> at its corner, not smaller than <paramref name="least"/> and not beyond the area.</summary>
        public static void MakeResizable(VisualElement window, VisualElement grip, Vector2 least, Action resized = null, Limits limits = null)
        {
            Vector2? start = null;
            var startSize = Vector2.zero;
            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                start = evt.position;
                startSize = new Vector2(window.layout.width, window.layout.height);
                grip.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!start.HasValue || !grip.HasPointerCapture(evt.pointerId) || window.parent == null)
                    return;
                var area = window.parent.contentRect;
                var room = new Vector2(Mathf.Max(0f, area.width - window.layout.x), Mathf.Max(0f, area.height - window.layout.y));
                if (limits != null)
                {
                    var most = FloatingWindowRules.MaxSize(new Vector2(area.width, area.height), limits.Margin, limits.BottomInset);
                    room = new Vector2(Mathf.Min(room.x, most.x), Mathf.Min(Mathf.Max(0f, room.y - limits.BottomInset), most.y));
                }
                var size = FloatingWindowRules.ClampSize(startSize + ((Vector2)evt.position - start.Value), least, room);
                window.style.width = size.x; // computed: window size from fit and saved geometry
                window.style.height = size.y; // computed: window size from fit and saved geometry
                resized?.Invoke();
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!grip.HasPointerCapture(evt.pointerId))
                    return;
                grip.ReleasePointer(evt.pointerId);
                start = null;
                evt.StopPropagation();
            });
        }
    }
}
