using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Keeps what a tool works on in sight (ADR 098, M4): the tool windows stand over the bottom of the map, so a junction, an object or a stop
    /// selected there is moved up into the free part above them, once per selection and only in the view from above.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        // Each request replaces the one before it, so a window that is slow to be laid out cannot move the view for a target long left.
        private int freeAreaTicket;

        /// <summary>
        /// The places of the parts of a tool window that are shown (its options column and its catalog), in panel coordinates. The window
        /// itself is a row as tall as its tallest part with the shorter ones at its bottom, so its own place would count empty space.
        /// </summary>
        private static List<Rect> ToolWindowRects(VisualElement window)
        {
            var rects = new List<Rect>();
            if (window == null || window.resolvedStyle.display == DisplayStyle.None || window.style.display == DisplayStyle.None)
                return rects;
            foreach (var part in window.Children())
            {
                if (part.style.display == DisplayStyle.None)
                    continue;
                var bound = part.worldBound;
                if (!float.IsNaN(bound.yMin) && bound.width >= 1f && bound.height >= 1f)
                    rects.Add(bound);
            }
            return rects;
        }

        /// <summary>
        /// Pans the view so the target stands in the free part of the map above <paramref name="window"/>, when it stands under the window or
        /// within <see cref="FreeAreaPan.MarginPixels"/> of it. Waits for the window to be laid out (a window shown just now has no place for a
        /// frame) and gives up after a second, when <paramref name="isOpen"/> says the tool was left, or when another request came.
        /// </summary>
        /// <param name="target">The target's place in world metres, read when the window is laid out; null when there is none.</param>
        private void KeepClearOfToolWindow(VisualElement window, Func<bool> isOpen, Func<OmsiMapPoint?> target)
        {
            if (window == null || mapCanvas == null)
                return;
            var ticket = ++freeAreaTicket;
            var tries = 0;
            var done = false;
            var viewAtRequest = mapCanvas.ViewState;
            var targetAtRequest = target();
            window.schedule.Execute(() =>
            {
                var now = target();
                var moved = targetAtRequest.HasValue && now.HasValue && FreeAreaRequest.Moved(targetAtRequest.Value.X, targetAtRequest.Value.Y, now.Value.X, now.Value.Y);
                var rects = ticket == freeAreaTicket && mapCanvas != null && isOpen() ? ToolWindowRects(window) : null;
                var dragging = mapCanvas != null && mapCanvas.IsDragging;
                var step = FreeAreaRequest.Next(rects == null, dragging, mapCanvas != null && mapCanvas.ViewState != viewAtRequest, moved, rects != null && rects.Count > 0);
                if (step == FreeAreaStep.Wait)
                {
                    // Waiting for the pointer to let go does not use up the tries; waiting for the window's layout does.
                    if (!dragging)
                        tries++;
                    return;
                }
                done = true;
                if (step == FreeAreaStep.Pan && now.HasValue)
                    mapCanvas.PanIntoFreeArea(now.Value, rects, FreeAreaPan.MarginPixels);
            }).Every(16).Until(() => done || tries >= 60);
        }

        /// <summary>For checks: -lineCentre n selects the stop n (from 0) of the line being planned as a click on the strip does and centres the map on it. True when it did.</summary>
        private bool RunLineCentreArgument(string[] args)
        {
            var at = Array.IndexOf(args, "-lineCentre");
            if (at < 0 || at + 1 >= args.Length || activeTool != EditorTool.Line || lineOptions == null ||
                !int.TryParse(args[at + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var index))
                return false;
            SelectLineStop(index, true);
            return true;
        }

        /// <summary>The place of an object in world metres.</summary>
        private OmsiMapPoint? ObjectWorldPoint(OmsiTileSummary tile, OmsiObjectRecord record)
        {
            return tile == null || record == null ? (OmsiMapPoint?)null : ObjectToWorld(tile, record, 0.0, 0.0);
        }

        /// <summary>The Objects window is open (placing or choosing an object) with the Single or Row choice.</summary>
        private bool ObjectsWindowOpen()
        {
            return objectsPanel != null && (objectsPanelOpen || activeTool == EditorTool.Rows) && objectsPanel.style.display != DisplayStyle.None;
        }

        /// <summary>The selected object (the last of several) is kept clear of the Objects window; used when the window opens and when the map selects.</summary>
        private void KeepSelectionClearOfObjectsWindow()
        {
            if (selection.Count == 0 || !ObjectsWindowOpen())
                return;
            var item = selection[selection.Count - 1];
            KeepClearOfToolWindow(objectsPanel, ObjectsWindowOpen, () => ObjectWorldPoint(item.Tile, item.Record));
        }
    }
}
