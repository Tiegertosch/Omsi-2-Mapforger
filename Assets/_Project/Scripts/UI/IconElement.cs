using System;
using System.Collections.Generic;
using System.Globalization;
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
        private SvgSign svg;

        public IconElement(string iconName)
        {
            AddToClassList("icon");
            pickingMode = PickingMode.Ignore;
            SetIcon(iconName);
            generateVisualContent += Draw;
            // A new colour, such as on an active tool, needs a new drawing.
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        /// <summary>
        /// Draws an SVG picture instead of the line icon (the tab icons the owner draws, ADR 099); null goes back to the icon. The picture is
        /// fitted into the square of the element, and its <c>currentColor</c> is the text colour, so an active tab keeps its colour.
        /// </summary>
        public void SetSvg(SvgSign sign)
        {
            svg = sign;
            MarkDirtyRepaint();
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
            if (svg != null)
            {
                svg.Draw(context.painter2D, size * 0.5f, Mathf.Min(size.x, size.y), resolvedStyle.color);
                return;
            }
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
        private const string Commands = "MmLlHhVvCcSsQqTtAaZz";

        /// <summary>
        /// Reads the path data. Only whitespace and commas may stand between numbers and commands; anything else, a number that is
        /// missing or too large, and a command that is not known make it a <see cref="FormatException"/>, so a path is never drawn half.
        /// </summary>
        public static void Parse(string data, List<PathStep> steps)
        {
            var position = 0;
            var command = 'M';
            var current = Vector2.zero;
            var start = Vector2.zero;
            var lastControl = Vector2.zero;
            var lastWasCubic = false;
            var lastWasQuadratic = false;

            void SkipGaps()
            {
                while (position < data.Length && (char.IsWhiteSpace(data[position]) || data[position] == ','))
                    position++;
            }

            float Next()
            {
                SkipGaps();
                var begin = position;
                if (position < data.Length && (data[position] == '-' || data[position] == '+'))
                    position++;
                var digits = 0;
                while (position < data.Length && data[position] >= '0' && data[position] <= '9')
                {
                    position++;
                    digits++;
                }
                if (position < data.Length && data[position] == '.')
                {
                    position++;
                    while (position < data.Length && data[position] >= '0' && data[position] <= '9')
                    {
                        position++;
                        digits++;
                    }
                }
                if (digits == 0)
                    throw new FormatException(begin >= data.Length ? "the path data ends where a number is expected" : "the path data has \"" + data[begin] + "\" where a number is expected");
                // An exponent counts only with digits after it; otherwise the letter is read as the next token.
                if (position < data.Length && (data[position] == 'e' || data[position] == 'E'))
                {
                    var look = position + 1;
                    if (look < data.Length && (data[look] == '-' || data[look] == '+'))
                        look++;
                    if (look < data.Length && data[look] >= '0' && data[look] <= '9')
                    {
                        while (look < data.Length && data[look] >= '0' && data[look] <= '9')
                            look++;
                        position = look;
                    }
                }
                var text = data.Substring(begin, position - begin);
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || float.IsNaN(value) || float.IsInfinity(value))
                    throw new FormatException("the path data has the number \"" + text + "\" which is out of range");
                return value;
            }

            // An arc flag is one character, 0 or 1, and needs no gap after it: "a5 5 0 011 1" reads 0, 1, 1, 1.
            bool Flag()
            {
                SkipGaps();
                if (position >= data.Length || (data[position] != '0' && data[position] != '1'))
                    throw new FormatException("an arc flag in the path data is not 0 or 1");
                return data[position++] == '1';
            }

            Vector2 Point(bool relative) => new Vector2(Next(), Next()) + (relative ? current : Vector2.zero);

            SkipGaps();
            while (position < data.Length)
            {
                if (char.IsLetter(data[position]))
                {
                    command = data[position++];
                    if (Commands.IndexOf(command) < 0)
                        throw new FormatException("Unsupported path command '" + command + "'.");
                }
                else if (char.ToUpperInvariant(command) == 'Z')
                    throw new FormatException("the path data has a number after a close command");
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
                        var largeArc = Flag();
                        var sweep = Flag();
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
                SkipGaps();
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
