using System;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A daily density curve drawn as a graph with a handle at each full hour (0 to 24): a click or drag sets the factor of the
    /// hour under the pointer, in steps of 0.05, between 0 and 3 (ADR 088). A line marks the factor 1.
    /// </summary>
    public sealed class TrafficCurveElement : VisualElement
    {
        public const double Step = 0.05;

        private static readonly Color Fill = new Color(0.95f, 0.55f, 0.15f, 0.22f);
        private static readonly Color Line = new Color(0.95f, 0.55f, 0.15f, 1f);
        private static readonly Color Grid = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color One = new Color(1f, 1f, 1f, 0.30f);
        private static readonly Color Changed = new Color(0.98f, 0.82f, 0.30f, 1f);

        private readonly double[] values = new double[OmsiTrafficCurve.HourCount];
        private double[] original = new double[OmsiTrafficCurve.HourCount];
        private int dragging = -1;

        public TrafficCurveElement()
        {
            style.height = 150;
            style.minHeight = 150;
            style.marginTop = 4;
            style.marginBottom = 2;
            AddToClassList("traffic-curve");
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => dragging = -1);
        }

        /// <summary>The hour and factor under the pointer or being dragged, for a read-out; -1 when the pointer leaves.</summary>
        public event Action<int> HourHovered;

        /// <summary>Raised after a handle was moved.</summary>
        public event Action ValuesChanged;

        public double[] Values => (double[])values.Clone();

        /// <summary>Shows a curve and remembers it as the one to compare with and to reset to.</summary>
        public void SetCurve(double[] hourly)
        {
            if (hourly == null || hourly.Length != values.Length)
                throw new ArgumentException("Expected a value for each hour from 0 to 24.", nameof(hourly));
            Array.Copy(hourly, values, values.Length);
            original = (double[])hourly.Clone();
            MarkDirtyRepaint();
        }

        public void Reset()
        {
            Array.Copy(original, values, values.Length);
            MarkDirtyRepaint();
            ValuesChanged?.Invoke();
        }

        /// <summary>True when a value differs from the curve shown first.</summary>
        public bool IsChanged
        {
            get
            {
                for (var hour = 0; hour < values.Length; hour++)
                    if (Math.Abs(values[hour] - original[hour]) > 1e-6)
                        return true;
                return false;
            }
        }

        public double Value(int hour)
        {
            return values[hour];
        }

        /// <summary>Sets the factor of an hour, snapped to a step and kept between 0 and the maximum.</summary>
        public void SetValue(int hour, double value)
        {
            hour = Mathf.Clamp(hour, 0, values.Length - 1);
            values[hour] = Math.Round(Math.Max(0.0, Math.Min(OmsiTrafficCurve.MaximumFactor, value)) / Step) * Step;
            MarkDirtyRepaint();
            ValuesChanged?.Invoke();
        }

        /// <summary>The top of the graph: 2, or 3 when some hour is above 2.</summary>
        private double Top
        {
            get
            {
                foreach (var value in values)
                    if (value > 2.0 + 1e-9)
                        return OmsiTrafficCurve.MaximumFactor;
                return 2.0;
            }
        }

        private float XOf(int hour)
        {
            return contentRect.width * hour / (values.Length - 1);
        }

        private float YOf(double value)
        {
            return contentRect.height * (float)(1.0 - value / Top);
        }

        private int HourAt(float x)
        {
            return Mathf.Clamp(Mathf.RoundToInt(x / Mathf.Max(1f, contentRect.width) * (values.Length - 1)), 0, values.Length - 1);
        }

        private double ValueAt(float y)
        {
            return (1.0 - y / Mathf.Max(1f, contentRect.height)) * Top;
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;
            dragging = HourAt(evt.localPosition.x);
            this.CapturePointer(evt.pointerId);
            SetValue(dragging, ValueAt(evt.localPosition.y));
            HourHovered?.Invoke(dragging);
            evt.StopPropagation();
        }

        private void OnMove(PointerMoveEvent evt)
        {
            var hour = HourAt(evt.localPosition.x);
            if (dragging >= 0 && this.HasPointerCapture(evt.pointerId))
            {
                SetValue(hour, ValueAt(evt.localPosition.y));
                dragging = hour;
            }
            HourHovered?.Invoke(hour);
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);
            dragging = -1;
        }

        private void Draw(MeshGenerationContext context)
        {
            var width = contentRect.width;
            var height = contentRect.height;
            if (width < 2f || height < 2f)
                return;
            var painter = context.painter2D;
            // Grid at every 6 hours and every half factor; the factor 1 stronger.
            painter.lineWidth = 1f;
            for (var hour = 0; hour < values.Length; hour += 6)
            {
                painter.strokeColor = Grid;
                painter.BeginPath();
                painter.MoveTo(new Vector2(XOf(hour), 0f));
                painter.LineTo(new Vector2(XOf(hour), height));
                painter.Stroke();
            }
            for (var factor = 0.5; factor <= Top + 1e-9; factor += 0.5)
            {
                painter.strokeColor = Math.Abs(factor - 1.0) < 1e-9 ? One : Grid;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, YOf(factor)));
                painter.LineTo(new Vector2(width, YOf(factor)));
                painter.Stroke();
            }
            // The area under the curve, then its line.
            painter.fillColor = Fill;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, height));
            for (var hour = 0; hour < values.Length; hour++)
                painter.LineTo(new Vector2(XOf(hour), YOf(values[hour])));
            painter.LineTo(new Vector2(width, height));
            painter.ClosePath();
            painter.Fill();
            painter.strokeColor = Line;
            painter.lineWidth = 2f;
            painter.BeginPath();
            for (var hour = 0; hour < values.Length; hour++)
            {
                var point = new Vector2(XOf(hour), YOf(values[hour]));
                if (hour == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }
            painter.Stroke();
            // A handle at each hour; the ones changed from the start are yellow.
            for (var hour = 0; hour < values.Length; hour++)
            {
                painter.fillColor = Math.Abs(values[hour] - original[hour]) > 1e-6 ? Changed : Line;
                painter.BeginPath();
                painter.Arc(new Vector2(XOf(hour), YOf(values[hour])), 3.5f, 0f, 360f);
                painter.Fill();
            }
        }
    }
}
