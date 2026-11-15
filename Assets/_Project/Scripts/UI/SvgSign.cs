using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A small picture read from an SVG file and drawn with Painter2D, for the signs the owner draws himself (ADR 099). Only a defined subset
    /// is read; anything else makes the whole file invalid, so a sign is never drawn half (the caller then falls back to the built-in drawing):
    /// <c>svg</c> with a <c>viewBox</c>; <c>g</c>, <c>path</c>, <c>circle</c>, <c>rect</c>, <c>polygon</c>; <c>transform</c> of <c>translate</c> and
    /// <c>scale</c> only; <c>fill</c>, <c>stroke</c>, <c>stroke-width</c>, <c>fill-rule</c>, <c>stroke-linecap</c> (butt, round) and
    /// <c>stroke-linejoin</c> as attributes, in <c>style</c> or in the simple class rules of a <c>&lt;style&gt;</c> block (<c>.name {fill:#fff}</c>, as
    /// CorelDRAW writes them; no other selectors), with solid colours (<c>#rgb</c>, <c>#rrggbb</c>, <c>rgb(r,g,b)</c>, a few names, <c>none</c>,
    /// <c>currentColor</c>). <c>defs</c> that hold only a <c>style</c>, <c>title</c>, <c>desc</c>, <c>metadata</c>, a DOCTYPE (it is not read) and
    /// elements of other namespaces (Inkscape's own) are skipped.
    /// </summary>
    internal sealed class SvgSign
    {
        private const string SvgNamespace = "http://www.w3.org/2000/svg";
        private const float CircleHandle = 0.5522847498f;

        private static readonly Dictionary<string, Color> NamedColours = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["black"] = Color.black, ["white"] = Color.white, ["red"] = new Color32(255, 0, 0, 255), ["yellow"] = new Color32(255, 255, 0, 255),
            ["blue"] = new Color32(0, 0, 255, 255), ["green"] = new Color32(0, 128, 0, 255), ["orange"] = new Color32(255, 165, 0, 255),
            ["gray"] = new Color32(128, 128, 128, 255), ["grey"] = new Color32(128, 128, 128, 255)
        };

        private static readonly Regex TransformPart = new Regex(@"\s*(translate|scale)\s*\(([^)]*)\)\s*,?", RegexOptions.Compiled);
        private static readonly char[] NumberSeparators = { ' ', ',', '\t', '\r', '\n' };

        private readonly List<Shape> shapes = new List<Shape>();

        private SvgSign(Rect viewBox)
        {
            ViewBox = viewBox;
        }

        /// <summary>The viewBox of the file: the part of its drawing that is placed in the box a sign is drawn in.</summary>
        public Rect ViewBox { get; }

        /// <summary>How many shapes the file holds (paths, circles, rectangles and polygons).</summary>
        public int ShapeCount => shapes.Count;

        /// <summary>The box around all points of the shapes in viewBox coordinates, curve controls included; for checks.</summary>
        public Rect Bounds
        {
            get
            {
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                void Take(Vector2 point)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
                foreach (var shape in shapes)
                    foreach (var step in shape.Steps)
                    {
                        if (step.Kind == PathStepKind.Close)
                            continue;
                        Take(step.A);
                        if (step.Kind == PathStepKind.Cubic)
                        {
                            Take(step.B);
                            Take(step.C);
                        }
                    }
                return shapes.Count == 0 ? default : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }

        /// <summary>Reads SVG text; false with the reason in <paramref name="error"/> when it is not valid or uses more than the subset.</summary>
        public static bool TryParse(string text, out SvgSign sign, out string error)
        {
            sign = null;
            error = null;
            try
            {
                sign = Parse(text);
                return true;
            }
            catch (FormatException exception)
            {
                error = exception.Message;
            }
            catch (XmlException exception)
            {
                error = "not valid XML (" + exception.Message + ")";
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IndexOutOfRangeException || exception is InvalidOperationException ||
                                              exception is ArgumentOutOfRangeException ||
                                              exception is ArithmeticException)
            {
                error = "malformed (" + exception.Message + ")";
            }
            return false;
        }

        /// <summary>Draws it fitted into a square of the given side around a centre, as large as fits with its proportions kept.</summary>
        /// <param name="current">The colour <c>currentColor</c> stands for, such as the text colour of an icon.</param>
        public void Draw(Painter2D painter, Vector2 centre, float side, Color current)
        {
            DrawScaled(painter, centre, Mathf.Min(side / ViewBox.width, side / ViewBox.height), current);
        }

        /// <summary>Draws it with its viewBox centred on a point, <paramref name="scale"/> pixels to a unit of the viewBox.</summary>
        public void DrawScaled(Painter2D painter, Vector2 centre, float scale, Color current)
        {
            var middle = ViewBox.center;
            foreach (var shape in shapes)
            {
                painter.BeginPath();
                foreach (var step in shape.Steps)
                    switch (step.Kind)
                    {
                        case PathStepKind.Move:
                            painter.MoveTo(centre + (step.A - middle) * scale);
                            break;
                        case PathStepKind.Line:
                            painter.LineTo(centre + (step.A - middle) * scale);
                            break;
                        case PathStepKind.Cubic:
                            painter.BezierCurveTo(centre + (step.A - middle) * scale, centre + (step.B - middle) * scale, centre + (step.C - middle) * scale);
                            break;
                        case PathStepKind.Close:
                            painter.ClosePath();
                            break;
                    }
                if (shape.Fill.Visible)
                {
                    painter.fillColor = shape.Fill.Current ? current : shape.Fill.Colour;
                    painter.Fill(shape.EvenOdd ? FillRule.OddEven : FillRule.NonZero);
                }
                if (shape.Stroke.Visible && shape.StrokeWidth > 0f)
                {
                    painter.strokeColor = shape.Stroke.Current ? current : shape.Stroke.Colour;
                    painter.lineWidth = shape.StrokeWidth * scale;
                    painter.lineCap = shape.Cap;
                    painter.lineJoin = shape.Join;
                    painter.Stroke();
                }
            }
        }

        // ---- Reading ----

        private struct Paint
        {
            public bool Visible;
            public bool Current;
            public Color Colour;
        }

        private struct Style
        {
            public Paint Fill;
            public Paint Stroke;
            public float StrokeWidth;
            public bool EvenOdd;
            public LineCap Cap;
            public LineJoin Join;
        }

        /// <summary>A scale and a shift, which is all <c>translate</c> and <c>scale</c> can make.</summary>
        private struct Placement
        {
            public float Sx;
            public float Sy;
            public float Tx;
            public float Ty;

            public static Placement Identity => new Placement { Sx = 1f, Sy = 1f };

            public Vector2 Apply(Vector2 point) => new Vector2(point.x * Sx + Tx, point.y * Sy + Ty);

            /// <summary>This placement applied after <paramref name="inner"/>: a point goes through the inner one first.</summary>
            public Placement After(Placement inner)
            {
                return new Placement { Sx = inner.Sx * Sx, Sy = inner.Sy * Sy, Tx = inner.Tx * Sx + Tx, Ty = inner.Ty * Sy + Ty };
            }

            public float LineScale => Mathf.Sqrt(Mathf.Abs(Sx * Sy));
        }

        private sealed class Shape
        {
            public readonly List<PathStep> Steps = new List<PathStep>();
            public Paint Fill;
            public Paint Stroke;
            public float StrokeWidth;
            public bool EvenOdd;
            public LineCap Cap;
            public LineJoin Join;
        }

        private static SvgSign Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("the file is empty");
            var document = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(text.TrimStart('﻿')), settings))
                document.Load(reader);
            var root = document.DocumentElement;
            if (root == null || root.LocalName != "svg" || !IsSvg(root))
                throw new FormatException("the root element is not <svg>");
            var viewBox = ReadViewBox(root.GetAttribute("viewBox"));
            var sign = new SvgSign(viewBox);
            var style = new Style
            {
                Fill = new Paint { Visible = true, Colour = Color.black }, Stroke = new Paint(), StrokeWidth = 1f, Cap = LineCap.Butt, Join = LineJoin.Miter
            };
            var rules = ReadClassRules(document);
            Visit(root, style, Placement.Identity, sign.shapes, rules, true);
            if (sign.shapes.Count == 0)
                throw new FormatException("the file draws nothing");
            return sign;
        }

        private static bool IsSvg(XmlElement element)
        {
            return element.NamespaceURI.Length == 0 || element.NamespaceURI == SvgNamespace;
        }

        private static Rect ReadViewBox(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("<svg> has no viewBox");
            var numbers = text.Split(NumberSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Length != 4)
                throw new FormatException("the viewBox needs four numbers");
            var x = Number(numbers[0], "viewBox");
            var y = Number(numbers[1], "viewBox");
            var width = Number(numbers[2], "viewBox");
            var height = Number(numbers[3], "viewBox");
            if (!(width > 0f) || !(height > 0f))
                throw new FormatException("the viewBox needs a width and a height above zero");
            return new Rect(x, y, width, height);
        }

        private static float Number(string text, string what)
        {
            var trimmed = text.Trim();
            if (trimmed.EndsWith("px", StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - 2);
            if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException(what + " has no number in \"" + text + "\"");
            return value;
        }

        private static void Visit(XmlElement element, Style inherited, Placement parent, List<Shape> shapes, Dictionary<string, string> rules, bool isRoot)
        {
            var style = ReadStyle(element, inherited, rules);
            var placement = parent;
            if (!isRoot && element.HasAttribute("transform"))
                placement = parent.After(ReadTransform(element.GetAttribute("transform")));
            switch (element.LocalName)
            {
                case "svg":
                case "g":
                    foreach (XmlNode node in element.ChildNodes)
                    {
                        if (!(node is XmlElement child) || !IsSvg(child))
                            continue;
                        switch (child.LocalName)
                        {
                            case "title":
                            case "desc":
                            case "metadata":
                            case "style":
                                // The class rules of a style block were read before the walk.
                                continue;
                            case "defs":
                                foreach (XmlNode inner in child.SelectNodes(".//*"))
                                    if (inner.LocalName != "style")
                                        throw new FormatException("<defs> with content is not supported (no gradients, no <use>)");
                                continue;
                        }
                        Visit(child, style, placement, shapes, rules, false);
                    }
                    break;
                case "path":
                {
                    var shape = NewShape(style, placement);
                    var data = element.GetAttribute("d");
                    if (string.IsNullOrWhiteSpace(data))
                        throw new FormatException("<path> has no d");
                    SvgPath.Parse(data, shape.Steps);
                    Place(shape, placement);
                    shapes.Add(shape);
                    break;
                }
                case "circle":
                {
                    var shape = NewShape(style, placement);
                    var centre = new Vector2(Optional(element, "cx"), Optional(element, "cy"));
                    var radius = Optional(element, "r");
                    if (!(radius > 0f))
                        throw new FormatException("<circle> needs r above zero");
                    var east = centre + new Vector2(radius, 0f);
                    var south = centre + new Vector2(0f, radius);
                    var west = centre - new Vector2(radius, 0f);
                    var north = centre - new Vector2(0f, radius);
                    var handle = radius * CircleHandle;
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Move, A = east });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Cubic, A = east + new Vector2(0f, handle), B = south + new Vector2(handle, 0f), C = south });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Cubic, A = south - new Vector2(handle, 0f), B = west + new Vector2(0f, handle), C = west });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Cubic, A = west - new Vector2(0f, handle), B = north - new Vector2(handle, 0f), C = north });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Cubic, A = north + new Vector2(handle, 0f), B = east - new Vector2(0f, handle), C = east });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Close });
                    Place(shape, placement);
                    shapes.Add(shape);
                    break;
                }
                case "rect":
                {
                    var shape = NewShape(style, placement);
                    if (Optional(element, "rx") != 0f || Optional(element, "ry") != 0f)
                        throw new FormatException("<rect> with rounded corners is not supported; use a <path>");
                    var x = Optional(element, "x");
                    var y = Optional(element, "y");
                    var width = Optional(element, "width");
                    var height = Optional(element, "height");
                    if (!(width > 0f) || !(height > 0f))
                        throw new FormatException("<rect> needs width and height above zero");
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Move, A = new Vector2(x, y) });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Line, A = new Vector2(x + width, y) });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Line, A = new Vector2(x + width, y + height) });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Line, A = new Vector2(x, y + height) });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Close });
                    Place(shape, placement);
                    shapes.Add(shape);
                    break;
                }
                case "polygon":
                {
                    var shape = NewShape(style, placement);
                    var numbers = element.GetAttribute("points").Split(NumberSeparators, StringSplitOptions.RemoveEmptyEntries);
                    if (numbers.Length < 6 || numbers.Length % 2 != 0)
                        throw new FormatException("<polygon> needs points of at least three corners");
                    for (var index = 0; index < numbers.Length; index += 2)
                        shape.Steps.Add(new PathStep
                        {
                            Kind = index == 0 ? PathStepKind.Move : PathStepKind.Line, A = new Vector2(Number(numbers[index], "points"), Number(numbers[index + 1], "points"))
                        });
                    shape.Steps.Add(new PathStep { Kind = PathStepKind.Close });
                    Place(shape, placement);
                    shapes.Add(shape);
                    break;
                }
                default:
                    throw new FormatException("<" + element.LocalName + "> is not supported (only svg, g, path, circle, rect and polygon)");
            }
        }

        private static float Optional(XmlElement element, string name)
        {
            return element.HasAttribute(name) ? Number(element.GetAttribute(name), name) : 0f;
        }

        private static Shape NewShape(Style style, Placement placement)
        {
            return new Shape
            {
                Fill = style.Fill, Stroke = style.Stroke, StrokeWidth = style.StrokeWidth * placement.LineScale, EvenOdd = style.EvenOdd, Cap = style.Cap, Join = style.Join
            };
        }

        // The shapes are stored in the coordinates of the viewBox, so the transforms of the groups are applied once, here.
        private static void Place(Shape shape, Placement placement)
        {
            for (var index = 0; index < shape.Steps.Count; index++)
            {
                var step = shape.Steps[index];
                step.A = placement.Apply(step.A);
                step.B = placement.Apply(step.B);
                step.C = placement.Apply(step.C);
                shape.Steps[index] = step;
            }
        }

        private static Placement ReadTransform(string text)
        {
            var result = Placement.Identity;
            var rest = TransformPart.Replace(text, match =>
            {
                var values = match.Groups[2].Value.Split(NumberSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length < 1 || values.Length > 2)
                    throw new FormatException("transform: " + match.Groups[1].Value + " needs one or two numbers");
                var first = Number(values[0], "transform");
                var second = values.Length == 2 ? Number(values[1], "transform") : (match.Groups[1].Value == "scale" ? first : 0f);
                var step = match.Groups[1].Value == "scale" ? new Placement { Sx = first, Sy = second } : new Placement { Sx = 1f, Sy = 1f, Tx = first, Ty = second };
                // Left to right, the first one is outermost: the point goes through the ones on its right first.
                result = result.After(step);
                return string.Empty;
            });
            if (rest.Trim().Length > 0)
                throw new FormatException("transform \"" + text.Trim() + "\" is not supported (only translate and scale)");
            return result;
        }

        // The rules of the <style> blocks as declarations by class name; only plain class selectors (".a" or ".a, .b") are understood.
        private static Dictionary<string, string> ReadClassRules(XmlDocument document)
        {
            var rules = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XmlNode node in document.GetElementsByTagName("style"))
            {
                var css = Regex.Replace(node.InnerText, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^}]*)\}"))
                    foreach (var selector in rule.Groups[1].Value.Split(','))
                    {
                        var name = selector.Trim();
                        if (name.Length < 2 || name[0] != '.' || name.IndexOfAny(new[] { ' ', '.', '#', ':', '[', '>', '+', '~' }, 1) >= 0)
                            throw new FormatException("the style selector \"" + name + "\" is not supported (only plain class rules such as .a {fill:#fff})");
                        var key = name.Substring(1);
                        rules[key] = rules.TryGetValue(key, out var before) ? before + ";" + rule.Groups[2].Value : rule.Groups[2].Value;
                    }
            }
            return rules;
        }

        private static Style ReadStyle(XmlElement element, Style inherited, Dictionary<string, string> rules)
        {
            var style = inherited;
            foreach (var name in new[] { "fill", "stroke", "stroke-width", "fill-rule", "stroke-linecap", "stroke-linejoin", "opacity", "fill-opacity", "stroke-opacity" })
                if (element.HasAttribute(name))
                    Apply(name, element.GetAttribute(name), ref style);
            // As in SVG the class rules win over the attributes and the style attribute wins over both.
            if (element.HasAttribute("class"))
                foreach (var name in element.GetAttribute("class").Split(NumberSeparators, StringSplitOptions.RemoveEmptyEntries))
                    if (rules.TryGetValue(name, out var fromClass))
                        ApplyDeclarations(fromClass, ref style);
            ApplyDeclarations(element.GetAttribute("style"), ref style);
            return style;
        }

        private static void ApplyDeclarations(string declarations, ref Style style)
        {
            if (declarations.Length == 0)
                return;
            foreach (var declaration in declarations.Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon > 0)
                    Apply(declaration.Substring(0, colon).Trim(), declaration.Substring(colon + 1).Trim(), ref style);
            }
        }

        private static void Apply(string name, string value, ref Style style)
        {
            switch (name)
            {
                case "fill":
                    style.Fill = ReadPaint(value);
                    break;
                case "stroke":
                    style.Stroke = ReadPaint(value);
                    break;
                case "stroke-width":
                    style.StrokeWidth = Number(value, "stroke-width");
                    break;
                case "fill-rule":
                    if (value != "nonzero" && value != "evenodd")
                        throw new FormatException("fill-rule \"" + value + "\" is not nonzero or evenodd");
                    style.EvenOdd = value == "evenodd";
                    break;
                case "stroke-linecap":
                    if (value != "butt" && value != "round")
                        throw new FormatException("stroke-linecap \"" + value + "\" is not supported (butt or round)");
                    style.Cap = value == "round" ? LineCap.Round : LineCap.Butt;
                    break;
                case "stroke-linejoin":
                    if (value != "miter" && value != "round" && value != "bevel")
                        throw new FormatException("stroke-linejoin \"" + value + "\" is not supported (miter, round or bevel)");
                    style.Join = value == "round" ? LineJoin.Round : value == "bevel" ? LineJoin.Bevel : LineJoin.Miter;
                    break;
                case "opacity":
                case "fill-opacity":
                case "stroke-opacity":
                    if (Math.Abs(Number(value, name) - 1f) > 1e-4f)
                        throw new FormatException(name + " other than 1 is not supported; use a solid colour");
                    break;
            }
        }

        private static Paint ReadPaint(string value)
        {
            var text = value.Trim();
            var lower = text.ToLowerInvariant();
            if (lower == "none")
                return new Paint();
            if (lower == "currentcolor")
                return new Paint { Visible = true, Current = true };
            if (NamedColours.TryGetValue(lower, out var named))
                return new Paint { Visible = true, Colour = named };
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                var hex = text.Substring(1);
                if (hex.Length == 3)
                    hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
                if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                    return new Paint { Visible = true, Colour = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255) };
            }
            else if (lower.StartsWith("rgb(", StringComparison.Ordinal) && lower.EndsWith(")", StringComparison.Ordinal))
            {
                var parts = lower.Substring(4, lower.Length - 5).Split(NumberSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var r) &&
                    byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var g) && byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                    return new Paint { Visible = true, Colour = new Color32(r, g, b, 255) };
            }
            throw new FormatException("the colour \"" + text + "\" is not supported (a #rrggbb colour, rgb(r,g,b), none or currentColor; no gradients)");
        }
    }
}
