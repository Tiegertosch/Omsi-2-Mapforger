using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A line icon drawn from SVG path data on a 24 × 24 grid, stroked like Lucide icons: 2 units wide with round caps and
    /// joins, in the element's text colour. It stays sharp at any UI scale and takes its colour from USS.
    /// </summary>
    internal sealed class IconElement : VisualElement
    {
        private const float GridSize = 24f;
        private const float StrokeWidth = 2f;

        // Parsed once per icon name; each icon is a list of subpaths, each a list of drawing steps.
        private static readonly Dictionary<string, List<PathStep>> Cache = new Dictionary<string, List<PathStep>>();

        private List<PathStep> steps;

        public IconElement(string iconName)
        {
            AddToClassList("icon");
            pickingMode = PickingMode.Ignore;
            SetIcon(iconName);
            generateVisualContent += Draw;
            // A new colour, such as on an active tool, needs a new drawing.
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        public void SetIcon(string iconName)
        {
            if (!Cache.TryGetValue(iconName, out steps))
            {
                if (!LucideIcons.Paths.TryGetValue(iconName, out var paths))
                    throw new ArgumentException("Unknown icon '" + iconName + "'.", nameof(iconName));
                steps = new List<PathStep>();
                foreach (var path in paths)
                    SvgPath.Parse(path, steps);
                Cache[iconName] = steps;
            }
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var size = contentRect.size;
            if (size.x <= 0f || size.y <= 0f || steps == null)
                return;
            var scale = Mathf.Min(size.x, size.y) / GridSize;
            var offset = (size - new Vector2(GridSize, GridSize) * scale) * 0.5f;
            Vector2 Map(Vector2 point) => offset + point * scale;

            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = StrokeWidth * scale;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            foreach (var step in steps)
            {
                switch (step.Kind)
                {
                    case PathStepKind.Move:
                        painter.MoveTo(Map(step.A));
                        break;
                    case PathStepKind.Line:
                        painter.LineTo(Map(step.A));
                        break;
                    case PathStepKind.Cubic:
                        painter.BezierCurveTo(Map(step.A), Map(step.B), Map(step.C));
                        break;
                    case PathStepKind.Close:
                        painter.ClosePath();
                        break;
                }
            }
            painter.Stroke();
        }
    }

    internal enum PathStepKind { Move, Line, Cubic, Close }

    /// <summary>One drawing step: move or line to A, a cubic curve through controls A and B to C, or closing the subpath.</summary>
    internal struct PathStep
    {
        public PathStepKind Kind;
        public Vector2 A;
        public Vector2 B;
        public Vector2 C;
    }

    /// <summary>
    /// Reads SVG path data into moves, lines and cubic curves: all commands in absolute and relative form, with
    /// quadratic curves raised to cubic ones and elliptical arcs split into cubic quarter arcs.
    /// </summary>
    internal static class SvgPath
    {
        private static readonly Regex Token = new Regex(@"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        public static void Parse(string data, List<PathStep> steps)
        {
            var tokens = new List<string>();
            foreach (Match match in Token.Matches(data))
                tokens.Add(match.Value);

            var index = 0;
            var command = 'M';
            var current = Vector2.zero;
            var start = Vector2.zero;
            var lastControl = Vector2.zero;
            var lastWasCubic = false;
            var lastWasQuadratic = false;

            float Next() => float.Parse(tokens[index++], NumberStyles.Float, CultureInfo.InvariantCulture);
            Vector2 Point(bool relative) => new Vector2(Next(), Next()) + (relative ? current : Vector2.zero);

            while (index < tokens.Count)
            {
                if (char.IsLetter(tokens[index][0]))
                    command = tokens[index++][0];
                var relative = char.IsLower(command);
                var cubic = false;
                var quadratic = false;
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        current = Point(relative);
                        start = current;
                        steps.Add(new PathStep { Kind = PathStepKind.Move, A = current });
                        // Further pairs after a move are lines.
                        command = relative ? 'l' : 'L';
                        break;
                    case 'L':
                        current = Point(relative);
                        steps.Add(new PathStep { Kind = PathStepKind.Line, A = current });
                        break;
                    case 'H':
                        current = new Vector2(Next() + (relative ? current.x : 0f), current.y);
                        steps.Add(new PathStep { Kind = PathStepKind.Line, A = current });
                        break;
                    case 'V':
                        current = new Vector2(current.x, Next() + (relative ? current.y : 0f));
                        steps.Add(new PathStep { Kind = PathStepKind.Line, A = current });
                        break;
                    case 'C':
                    {
                        var first = Point(relative);
                        var second = Point(relative);
                        var end = Point(relative);
                        AddCubic(steps, first, second, end);
                        lastControl = second;
                        current = end;
                        cubic = true;
                        break;
                    }
                    case 'S':
                    {
                        var first = lastWasCubic ? current * 2f - lastControl : current;
                        var second = Point(relative);
                        var end = Point(relative);
                        AddCubic(steps, first, second, end);
                        lastControl = second;
                        current = end;
                        cubic = true;
                        break;
                    }
                    case 'Q':
                    {
                        var control = Point(relative);
                        var end = Point(relative);
                        AddQuadratic(steps, current, control, end);
                        lastControl = control;
                        current = end;
                        quadratic = true;
                        break;
                    }
                    case 'T':
                    {
                        var control = lastWasQuadratic ? current * 2f - lastControl : current;
                        var end = Point(relative);
                        AddQuadratic(steps, current, control, end);
                        lastControl = control;
                        current = end;
                        quadratic = true;
                        break;
                    }
                    case 'A':
                    {
                        var radiusX = Next();
                        var radiusY = Next();
                        var rotation = Next();
                        var largeArc = Next() != 0f;
                        var sweep = Next() != 0f;
                        var end = Point(relative);
                        AddArc(steps, current, end, radiusX, radiusY, rotation, largeArc, sweep);
                        current = end;
                        break;
                    }
                    case 'Z':
                        steps.Add(new PathStep { Kind = PathStepKind.Close });
                        current = start;
                        break;
                    default:
                        throw new FormatException("Unsupported path command '" + command + "'.");
                }
                lastWasCubic = cubic;
                lastWasQuadratic = quadratic;
            }
        }

        private static void AddCubic(List<PathStep> steps, Vector2 first, Vector2 second, Vector2 end)
        {
            steps.Add(new PathStep { Kind = PathStepKind.Cubic, A = first, B = second, C = end });
        }

        private static void AddQuadratic(List<PathStep> steps, Vector2 from, Vector2 control, Vector2 end)
        {
            AddCubic(steps, from + (control - from) * (2f / 3f), end + (control - end) * (2f / 3f), end);
        }

        /// <summary>The SVG endpoint arc as cubic curves of at most a quarter turn each (SVG 1.1, appendix F.6).</summary>
        private static void AddArc(List<PathStep> steps, Vector2 from, Vector2 to, float radiusX, float radiusY, float rotationDegrees,
            bool largeArc, bool sweep)
        {
            if (from == to)
                return;
            radiusX = Mathf.Abs(radiusX);
            radiusY = Mathf.Abs(radiusY);
            if (radiusX < 1e-6f || radiusY < 1e-6f)
            {
                steps.Add(new PathStep { Kind = PathStepKind.Line, A = to });
                return;
            }

            var phi = rotationDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(phi);
            var sin = Mathf.Sin(phi);
            var half = (from - to) * 0.5f;
            var x1 = cos * half.x + sin * half.y;
            var y1 = -sin * half.x + cos * half.y;

            // Radii too small to reach the end point grow just enough.
            var lambda = x1 * x1 / (radiusX * radiusX) + y1 * y1 / (radiusY * radiusY);
            if (lambda > 1f)
            {
                var grow = Mathf.Sqrt(lambda);
                radiusX *= grow;
                radiusY *= grow;
            }

            var numerator = radiusX * radiusX * radiusY * radiusY - radiusX * radiusX * y1 * y1 - radiusY * radiusY * x1 * x1;
            var denominator = radiusX * radiusX * y1 * y1 + radiusY * radiusY * x1 * x1;
            var factor = Mathf.Sqrt(Mathf.Max(0f, numerator / denominator)) * (largeArc == sweep ? -1f : 1f);
            var cx1 = factor * radiusX * y1 / radiusY;
            var cy1 = -factor * radiusY * x1 / radiusX;
            var centre = new Vector2(cos * cx1 - sin * cy1, sin * cx1 + cos * cy1) + (from + to) * 0.5f;

            var startAngle = Mathf.Atan2((y1 - cy1) / radiusY, (x1 - cx1) / radiusX);
            var endAngle = Mathf.Atan2((-y1 - cy1) / radiusY, (-x1 - cx1) / radiusX);
            var delta = endAngle - startAngle;
            if (sweep && delta < 0f)
                delta += 2f * Mathf.PI;
            else if (!sweep && delta > 0f)
                delta -= 2f * Mathf.PI;

            var segments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(delta) / (Mathf.PI * 0.5f) - 1e-4f));
            var step = delta / segments;
            var handle = 4f / 3f * Mathf.Tan(step / 4f);
            Vector2 OnEllipse(float angle) => centre + new Vector2(cos * radiusX * Mathf.Cos(angle) - sin * radiusY * Mathf.Sin(angle),
                sin * radiusX * Mathf.Cos(angle) + cos * radiusY * Mathf.Sin(angle));
            Vector2 Tangent(float angle) => new Vector2(-cos * radiusX * Mathf.Sin(angle) - sin * radiusY * Mathf.Cos(angle),
                -sin * radiusX * Mathf.Sin(angle) + cos * radiusY * Mathf.Cos(angle));

            var angleA = startAngle;
            var pointA = from;
            for (var segment = 0; segment < segments; segment++)
            {
                var angleB = angleA + step;
                var pointB = segment == segments - 1 ? to : OnEllipse(angleB);
                AddCubic(steps, pointA + Tangent(angleA) * handle, pointB - Tangent(angleB) * handle, pointB);
                angleA = angleB;
                pointA = pointB;
            }
        }
    }
}
