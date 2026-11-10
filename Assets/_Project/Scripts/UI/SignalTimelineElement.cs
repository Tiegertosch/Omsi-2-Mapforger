using System;
using System.Collections.Generic;
using System.Globalization;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The timeline of a traffic light program (ADR 085, milestone 3): one row per signal group over the whole cycle, each
    /// stretch in the colour its car head shows, the stop and jump points as marks. A click selects a stretch; where it may
    /// be changed, dragging the edge between two stretches moves it, and dragging anywhere else along a row asks the owner
    /// to paint that stretch of time (<see cref="PaintRequested"/>), both to whole seconds (with Shift to tenths). Edges are
    /// moved in the program in place; the owner writes it.
    /// </summary>
    internal sealed class SignalTimelineElement : VisualElement
    {
        /// <summary>An edge counts as under the pointer within this many pixels.</summary>
        private const float EdgePixels = 5f;

        /// <summary>A press that moves less than this many pixels is a click, not a paint.</summary>
        private const float PaintPixels = 4f;

        private const float RowHeight = 22f;

        private static readonly Color Red = new Color(0.86f, 0.24f, 0.24f);
        private static readonly Color Yellow = new Color(0.96f, 0.76f, 0.20f);
        private static readonly Color Green = new Color(0.25f, 0.73f, 0.36f);
        private static readonly Color Dark = new Color(0.22f, 0.24f, 0.28f);

        private readonly VisualElement axis;
        private readonly VisualElement rows;
        private OmsiSignalProgram program;
        private Func<int, Color> groupColour;
        private bool editable;

        public SignalTimelineElement()
        {
            AddToClassList("signal-timeline");
            axis = new VisualElement();
            axis.AddToClassList("signal-timeline-axis");
            Add(axis);
            rows = new VisualElement();
            Add(rows);
        }

        public int SelectedLight { get; private set; } = -1;
        public int SelectedSegment { get; private set; } = -1;

        /// <summary>The second last clicked in the selected row (where Split cuts), or -1.</summary>
        public double ClickedSeconds { get; private set; } = -1;

        /// <summary>The colour a drag along a row paints with, shown while dragging.</summary>
        public Color BrushColour { get; set; } = Green;

        /// <summary>A stretch was clicked.</summary>
        public event Action SelectionChanged;

        /// <summary>An edge was dragged to a new place; the program has changed.</summary>
        public event Action Edited;

        /// <summary>A drag along a row: the light and the stretch of time, in order, snapped; the owner paints it.</summary>
        public event Action<int, double, double> PaintRequested;

        public static Color ColourOf(int state)
        {
            switch (OmsiSignalAspects.Of(state))
            {
                case OmsiSignalAspect.Red: return Red;
                case OmsiSignalAspect.Yellow: return Yellow;
                case OmsiSignalAspect.Green:
                case OmsiSignalAspect.GreenYellow: return Green;
                case OmsiSignalAspect.RedYellow: return Red;
                default: return Dark;
            }
        }

        public void Show(OmsiSignalProgram shown, Func<int, Color> colourOfGroup, bool canEdit)
        {
            program = shown;
            groupColour = colourOfGroup;
            editable = canEdit;
            if (program == null || SelectedLight >= program.Lights.Count)
            {
                SelectedLight = -1;
                SelectedSegment = -1;
            }
            Rebuild();
        }

        public void Select(int light, int segment)
        {
            SelectedLight = light;
            SelectedSegment = segment;
            foreach (var child in rows.Children())
                child.Q<VisualElement>(className: "signal-timeline-bar")?.MarkDirtyRepaint();
            SelectionChanged?.Invoke();
        }

        private void Rebuild()
        {
            axis.Clear();
            rows.Clear();
            if (program == null || program.Lights.Count == 0)
                return;
            var cycle = program.CycleLength;
            var step = cycle <= 40 ? 5 : cycle <= 120 ? 10 : 30;
            var spacer = new VisualElement();
            spacer.AddToClassList("signal-timeline-name");
            axis.Add(spacer);
            var scale = new VisualElement();
            scale.AddToClassList("signal-timeline-scale");
            axis.Add(scale);
            for (var second = 0; second <= cycle + 1e-6; second += step)
            {
                var tick = new Label(second.ToString(CultureInfo.InvariantCulture));
                tick.AddToClassList("signal-timeline-tick");
                tick.style.left = Length.Percent((float)(100.0 * second / cycle));
                scale.Add(tick);
            }
            for (var light = 0; light < program.Lights.Count; light++)
            {
                var row = new VisualElement();
                row.AddToClassList("signal-timeline-row");
                var chip = new VisualElement();
                chip.AddToClassList("signal-group-chip");
                chip.style.backgroundColor = groupColour(light);
                row.Add(chip);
                var name = new Label(program.Lights[light].Name);
                name.AddToClassList("signal-timeline-name");
                row.Add(name);
                row.Add(new Bar(this, light));
                rows.Add(row);
            }
        }

        /// <summary>The bar of one group: its stretches, drawn and clicked.</summary>
        private sealed class Bar : VisualElement
        {
            private readonly SignalTimelineElement owner;
            private readonly int light;
            private int dragging = -1;
            private bool pressing;
            private bool painting;
            private float pressX;
            private float pointerX;

            public Bar(SignalTimelineElement owner, int light)
            {
                this.owner = owner;
                this.light = light;
                AddToClassList("signal-timeline-bar");
                style.height = RowHeight;
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
            }

            private double SecondsAt(float x)
            {
                var width = Mathf.Max(1f, contentRect.width);
                return Mathf.Clamp01(x / width) * owner.program.CycleLength;
            }

            private float XAt(double seconds)
            {
                return (float)(seconds / owner.program.CycleLength) * contentRect.width;
            }

            private void Draw(MeshGenerationContext context)
            {
                if (owner.program == null || light >= owner.program.Lights.Count)
                    return;
                var painter = context.painter2D;
                var segments = OmsiSignalTimeline.Segments(owner.program, light);
                var height = contentRect.height;
                for (var index = 0; index < segments.Count; index++)
                {
                    var segment = segments[index];
                    var left = XAt(segment.Start);
                    var right = Mathf.Max(left + 1f, XAt(segment.End));
                    var aspect = OmsiSignalAspects.Of(segment.State);
                    // Red and yellow together: red above, yellow below.
                    Fill(painter, left, right, 0f, aspect == OmsiSignalAspect.RedYellow ? height * 0.5f : height, ColourOf(segment.State));
                    if (aspect == OmsiSignalAspect.RedYellow)
                        Fill(painter, left, right, height * 0.5f, height, Yellow);
                    if (aspect == OmsiSignalAspect.GreenYellow)
                        Fill(painter, left, right, height * 0.5f, height, Yellow);
                    if (index > 0)
                        Fill(painter, left - 0.5f, left + 0.5f, 0f, height, new Color(0.1f, 0.11f, 0.13f, 0.9f));
                }
                if (owner.SelectedLight == light && owner.SelectedSegment >= 0 && owner.SelectedSegment < segments.Count)
                {
                    var selected = segments[owner.SelectedSegment];
                    painter.strokeColor = Color.white;
                    painter.lineWidth = 2f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(XAt(selected.Start) + 1f, 1f));
                    painter.LineTo(new Vector2(XAt(selected.End) - 1f, 1f));
                    painter.LineTo(new Vector2(XAt(selected.End) - 1f, height - 1f));
                    painter.LineTo(new Vector2(XAt(selected.Start) + 1f, height - 1f));
                    painter.ClosePath();
                    painter.Stroke();
                }
                // The stretch being painted, in the brush colour with a white frame.
                if (painting)
                {
                    var left = Mathf.Min(pressX, pointerX);
                    var right = Mathf.Max(pressX, pointerX);
                    var brush = owner.BrushColour;
                    Fill(painter, left, right, 3f, height - 3f, new Color(brush.r, brush.g, brush.b, 0.85f));
                    painter.strokeColor = Color.white;
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(left, 2f));
                    painter.LineTo(new Vector2(right, 2f));
                    painter.LineTo(new Vector2(right, height - 2f));
                    painter.LineTo(new Vector2(left, height - 2f));
                    painter.ClosePath();
                    painter.Stroke();
                }
                // Stop and jump points of this light: a white mark at their second, a jump with a line to where it goes.
                foreach (var hold in owner.program.Holds)
                {
                    if (hold.Light != light)
                        continue;
                    var x = XAt(Math.Min(hold.Seconds, owner.program.CycleLength));
                    painter.fillColor = Color.white;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x - 4f, 0f));
                    painter.LineTo(new Vector2(x + 4f, 0f));
                    painter.LineTo(new Vector2(x, 6f));
                    painter.ClosePath();
                    painter.Fill();
                    if (hold.JumpTo.HasValue)
                    {
                        painter.strokeColor = Color.white;
                        painter.lineWidth = 1f;
                        painter.BeginPath();
                        painter.MoveTo(new Vector2(x, 3f));
                        painter.LineTo(new Vector2(XAt(Math.Min(hold.JumpTo.Value, owner.program.CycleLength)), 3f));
                        painter.Stroke();
                    }
                }
            }

            private static void Fill(Painter2D painter, float left, float right, float top, float bottom, Color colour)
            {
                painter.fillColor = colour;
                painter.BeginPath();
                painter.MoveTo(new Vector2(left, top));
                painter.LineTo(new Vector2(right, top));
                painter.LineTo(new Vector2(right, bottom));
                painter.LineTo(new Vector2(left, bottom));
                painter.ClosePath();
                painter.Fill();
            }

            private void OnDown(PointerDownEvent evt)
            {
                if (owner.program == null || evt.button != 0)
                    return;
                var segments = OmsiSignalTimeline.Segments(owner.program, light);
                var x = evt.localPosition.x;
                // An edge first, then the stretch under the pointer.
                if (owner.editable)
                    for (var index = 1; index < segments.Count; index++)
                        if (Mathf.Abs(XAt(segments[index].Start) - x) <= EdgePixels)
                        {
                            dragging = index;
                            this.CapturePointer(evt.pointerId);
                            owner.Select(light, index);
                            evt.StopPropagation();
                            return;
                        }
                var seconds = SecondsAt(x);
                owner.ClickedSeconds = seconds;
                owner.Select(light, segments.FindIndex(segment => seconds >= segment.Start && seconds < segment.End));
                if (owner.editable)
                {
                    // Perhaps the start of a paint: it becomes one once the pointer has moved a few pixels.
                    pressing = true;
                    painting = false;
                    pressX = x;
                    pointerX = x;
                    this.CapturePointer(evt.pointerId);
                }
                evt.StopPropagation();
            }

            private double Snap(float x, bool tenths)
            {
                var seconds = SecondsAt(x);
                return tenths ? Math.Round(seconds, 1) : Math.Round(seconds);
            }

            private void OnMove(PointerMoveEvent evt)
            {
                if (pressing && this.HasPointerCapture(evt.pointerId))
                {
                    pointerX = Mathf.Clamp(evt.localPosition.x, 0f, contentRect.width);
                    if (!painting && Mathf.Abs(pointerX - pressX) >= PaintPixels)
                        painting = true;
                    if (painting)
                    {
                        // Show the snapped stretch, so what is drawn is what will be painted.
                        pointerX = XAt(Snap(pointerX, evt.shiftKey));
                        MarkDirtyRepaint();
                    }
                    return;
                }
                if (dragging < 0 || !this.HasPointerCapture(evt.pointerId))
                    return;
                var seconds = SecondsAt(evt.localPosition.x);
                seconds = evt.shiftKey ? Math.Round(seconds, 1) : Math.Round(seconds);
                var segments = OmsiSignalTimeline.Segments(owner.program, light);
                if (dragging >= segments.Count || Math.Abs(segments[dragging].Start - seconds) < 1e-6)
                    return;
                OmsiSignalTimeline.MoveBoundary(owner.program, light, dragging, seconds);
                // The stretch dragged may have gone; the edge then belongs to the next pair.
                var now = OmsiSignalTimeline.Segments(owner.program, light);
                dragging = Math.Min(dragging, now.Count - 1);
                if (dragging < 1)
                    dragging = -1;
                owner.SelectedSegment = Math.Max(0, Math.Min(owner.SelectedSegment, now.Count - 1));
                MarkDirtyRepaint();
                owner.Edited?.Invoke();
            }

            private void OnUp(PointerUpEvent evt)
            {
                if (!this.HasPointerCapture(evt.pointerId))
                    return;
                this.ReleasePointer(evt.pointerId);
                dragging = -1;
                if (pressing && painting)
                {
                    pressing = false;
                    painting = false;
                    MarkDirtyRepaint();
                    var from = Snap(pressX, evt.shiftKey);
                    var to = Snap(Mathf.Clamp(evt.localPosition.x, 0f, contentRect.width), evt.shiftKey);
                    if (Math.Abs(to - from) >= 0.1)
                        owner.PaintRequested?.Invoke(light, Math.Min(from, to), Math.Max(from, to));
                    return;
                }
                pressing = false;
                painting = false;
                owner.SelectionChanged?.Invoke();
            }
        }
    }
}
