using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// M6 step 0: computes WCAG contrast ratios for the --mf-* tokens of EditorShell.uss against the surfaces they stand on
    /// (text 4.5:1, symbols 3:1) and for the glass surfaces over a light map ground. The stylesheet is read as a file.
    /// </summary>
    public sealed class UiTokenContrastTests
    {
        private const double TextRatio = 4.5;
        private const double SymbolRatio = 3.0;

        private static readonly string[] Surfaces = { "bg", "panel", "panel-raised", "field" };
        private static readonly string[] TextTokens = { "text-primary", "text-secondary", "text-muted", "accent", "success", "warning", "error", "info" };

        private static string stylesheet;
        private static Dictionary<string, Rgba> tokens;

        private struct Rgba
        {
            public double R, G, B, A;
        }

        [OneTimeSetUp]
        public void ReadStylesheet()
        {
            var path = Path.Combine(Application.dataPath, "_Project", "UI", "Resources", "EditorShell.uss");
            Assert.IsTrue(File.Exists(path), path);
            stylesheet = File.ReadAllText(path);
            tokens = ReadTokens(stylesheet);
        }

        [Test]
        public void ColorTokensAreParsed()
        {
            foreach (var name in new[] { "bg", "panel", "panel-raised", "panel-hover", "field", "text-primary", "text-secondary", "text-muted", "text-disabled", "accent", "accent-hover", "success", "warning", "error", "info", "bar-glass", "tool-glass", "panel-glass" })
                Assert.IsTrue(tokens.ContainsKey(name), "Missing token --mf-color-" + name);
        }

        [Test]
        public void TextTokensReachFourPointFiveOnTheirSurfaces()
        {
            var failures = new StringBuilder();
            foreach (var text in TextTokens)
                foreach (var surface in Surfaces)
                    Check(failures, text, surface, TextRatio);
            Assert.AreEqual(string.Empty, failures.ToString(), "Text tokens below 4.5:1:\n" + failures);
        }

        [Test]
        public void PrimaryAndSecondaryTextStayReadableOnTheHoverSurface()
        {
            var failures = new StringBuilder();
            Check(failures, "text-primary", "panel-hover", TextRatio);
            Check(failures, "text-secondary", "panel-hover", TextRatio);
            Assert.AreEqual(string.Empty, failures.ToString(), failures.ToString());
        }

        [Test]
        public void SymbolsReachThreeOnTheHoverSurface()
        {
            var failures = new StringBuilder();
            foreach (var symbol in new[] { "accent", "success", "warning", "error", "info", "text-muted" })
                Check(failures, symbol, "panel-hover", SymbolRatio);
            Assert.AreEqual(string.Empty, failures.ToString(), failures.ToString());
        }

        [Test]
        public void DisabledTextKeepsThreeToOneOnTheDarkSurfaces()
        {
            var failures = new StringBuilder();
            foreach (var surface in new[] { "bg", "panel", "panel-raised" })
                Check(failures, "text-disabled", surface, SymbolRatio);
            Assert.AreEqual(string.Empty, failures.ToString(), failures.ToString());
        }

        [Test]
        public void DarkTextOnAccentAndStatusFillsReachFourPointFive()
        {
            var failures = new StringBuilder();
            foreach (var fill in new[] { "accent", "accent-hover", "success", "warning", "error" })
                Check(failures, "bg", fill, TextRatio);
            Assert.AreEqual(string.Empty, failures.ToString(), failures.ToString());
        }

        [Test]
        public void ButtonGlassOverWhiteKeepsPrimaryAndSecondaryTextReadable()
        {
            var failures = new StringBuilder();
            var white = new Rgba { R = 255, G = 255, B = 255, A = 1 };
            foreach (var glass in new[] { "tool-glass", "bar-glass" })
                CheckAgainst(failures, "text-primary over " + glass + " on white", tokens["text-primary"], Over(tokens[glass], white), TextRatio);
            CheckAgainst(failures, "text-secondary over tool-glass on white", tokens["text-secondary"], Over(tokens["tool-glass"], white), TextRatio);
            Assert.AreEqual(string.Empty, failures.ToString(), failures.ToString());
        }

        [Test]
        public void PanelGlassOverALightGroundKeepsTextReadable()
        {
            var failures = new StringBuilder();
            var light = new Rgba { R = 0xC8, G = 0xD0, B = 0xB0, A = 1 };
            var surface = Over(tokens["panel-glass"], light);
            foreach (var text in new[] { "text-primary", "text-secondary", "text-muted", "error" })
                CheckAgainst(failures, text + " over panel-glass on #C8D0B0", tokens[text], surface, TextRatio);
            Assert.AreEqual(string.Empty, failures.ToString(), failures.ToString());
        }

        [Test]
        public void SwitchedOffStarsAndOrnamentalSymbolsReachThreeToOne()
        {
            var failures = new StringBuilder();
            foreach (var selector in new[] { ".favorite-button", ".dlc-mark-button", ".floating-panel-icon", ".welcome-recent-icon" })
            {
                var token = RuleColorToken(selector);
                Assert.IsNotNull(token, selector + " has no color: var(--mf-color-...)");
                Assert.AreNotEqual("border", token, selector + " still uses the border token (1.7:1)");
                foreach (var surface in new[] { "panel", "panel-raised" })
                    Check(failures, token, surface, SymbolRatio, selector);
            }
            Assert.AreEqual(string.Empty, failures.ToString(), failures.ToString());
        }

        [Test]
        public void DisabledRulesUseTheDisabledToken()
        {
            var offenders = new List<string>();
            foreach (Match match in Regex.Matches(stylesheet, @"(?m)^([^{\n]*:disabled[^{\n]*)\{([^}\n]*)\}"))
                if (match.Groups[2].Value.Contains("var(--mf-color-text-muted)"))
                    offenders.Add(match.Groups[1].Value.Trim());
            Assert.IsEmpty(offenders, ":disabled rules that still use text-muted: " + string.Join(" | ", offenders));
        }

        [Test]
        public void ContrastMathMatchesKnownValues()
        {
            var black = new Rgba { R = 0, G = 0, B = 0, A = 1 };
            var white = new Rgba { R = 255, G = 255, B = 255, A = 1 };
            Assert.AreEqual(21.0, Contrast(white, black), 0.001);
            Assert.AreEqual(1.0, Contrast(white, white), 0.001);
            // Value of the design review (DesignReview-2026-10-28.md): the old muted text on the panel
            Assert.AreEqual(4.33, Contrast(Parse("#7E8A99"), Parse("#202630")), 0.01);
        }

        private static void Check(StringBuilder failures, string text, string surface, double minimum, string where = null)
        {
            var ratio = Contrast(tokens[text], tokens[surface]);
            if (ratio < minimum)
                failures.AppendLine((where != null ? where + ": " : string.Empty) + text + " on " + surface + " = " + ratio.ToString("0.00", CultureInfo.InvariantCulture) + " (< " + minimum.ToString("0.0", CultureInfo.InvariantCulture) + ")");
        }

        private static void CheckAgainst(StringBuilder failures, string label, Rgba foreground, Rgba surface, double minimum)
        {
            var ratio = Contrast(foreground, surface);
            if (ratio < minimum)
                failures.AppendLine(label + " = " + ratio.ToString("0.00", CultureInfo.InvariantCulture) + " (< " + minimum.ToString("0.0", CultureInfo.InvariantCulture) + ")");
        }

        private static Dictionary<string, Rgba> ReadTokens(string css)
        {
            var result = new Dictionary<string, Rgba>(StringComparer.Ordinal);
            var rootStart = css.IndexOf(":root", StringComparison.Ordinal);
            var rootEnd = css.IndexOf("\n}", rootStart, StringComparison.Ordinal);
            var root = css.Substring(rootStart, rootEnd - rootStart);
            foreach (Match match in Regex.Matches(root, @"--mf-color-([\w-]+)\s*:\s*([^;]+);"))
            {
                var value = match.Groups[2].Value.Trim();
                if (value.StartsWith("#", StringComparison.Ordinal) || value.StartsWith("rgba", StringComparison.Ordinal))
                    result[match.Groups[1].Value] = Parse(value);
            }
            return result;
        }

        private static string RuleColorToken(string selector)
        {
            var match = Regex.Match(stylesheet, "(?m)^" + Regex.Escape(selector) + @"\s*\{([^}]*)\}");
            if (!match.Success)
                return null;
            var color = Regex.Match(match.Groups[1].Value, @"(?<![-\w])color\s*:\s*var\(--mf-color-([\w-]+)\)");
            return color.Success ? color.Groups[1].Value : null;
        }

        private static Rgba Parse(string value)
        {
            if (value.StartsWith("#", StringComparison.Ordinal))
                return new Rgba
                {
                    R = int.Parse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    G = int.Parse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    B = int.Parse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    A = 1
                };
            var parts = value.Substring(value.IndexOf('(') + 1).TrimEnd(')').Split(',');
            return new Rgba
            {
                R = double.Parse(parts[0], CultureInfo.InvariantCulture),
                G = double.Parse(parts[1], CultureInfo.InvariantCulture),
                B = double.Parse(parts[2], CultureInfo.InvariantCulture),
                A = double.Parse(parts[3], CultureInfo.InvariantCulture)
            };
        }

        private static Rgba Over(Rgba top, Rgba ground)
        {
            return new Rgba
            {
                R = top.R * top.A + ground.R * (1 - top.A),
                G = top.G * top.A + ground.G * (1 - top.A),
                B = top.B * top.A + ground.B * (1 - top.A),
                A = 1
            };
        }

        private static double Luminance(Rgba color)
        {
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        private static double Linear(double channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static double Contrast(Rgba a, Rgba b)
        {
            var la = Luminance(a);
            var lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
    }
}
