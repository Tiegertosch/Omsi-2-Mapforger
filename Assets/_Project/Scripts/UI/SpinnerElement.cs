using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A loading spinner: a faint ring with a turning arc in the element's text colour, such as the accent, drawn with the
    /// stroke width of the icons. It turns only while it is on a panel.
    /// </summary>
    internal sealed class SpinnerElement : VisualElement
    {
        private const float StrokeWidth = 2.5f;
        private const float ArcDegrees = 100f;
        private const float DegreesPerSecond = 300f;

        private IVisualElementScheduledItem turning;

        public SpinnerElement()
        {
            AddToClassList("spinner");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<AttachToPanelEvent>(_ => turning = schedule.Execute(MarkDirtyRepaint).Every(33));
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                turning?.Pause();
                turning = null;
            });
        }

        private void Draw(MeshGenerationContext context)
        {
            var size = Mathf.Min(contentRect.width, contentRect.height);
            if (size <= StrokeWidth * 2f)
                return;
            var centre = contentRect.center;
            var radius = size * 0.5f - StrokeWidth;
            var colour = resolvedStyle.color;
            var painter = context.painter2D;
            painter.lineWidth = StrokeWidth;
            painter.lineCap = LineCap.Round;

            painter.strokeColor = new Color(colour.r, colour.g, colour.b, colour.a * 0.2f);
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();

            var start = Time.realtimeSinceStartup * DegreesPerSecond % 360f;
            painter.strokeColor = colour;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(start), Angle.Degrees(start + ArcDegrees));
            painter.Stroke();
        }
    }
}
