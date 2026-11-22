using System.IO;
using System.Text.RegularExpressions;
using System;
using NUnit.Framework;
using OmsiMapEditor.UI;
using UnityEngine;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// M6 step 4: windows and dialogs with a fixed width must be able to shrink in a small window or at a high interface scale, so each
    /// one states a maximum in percent of its parent; the menus of the menu bar are placed by code under their buttons. The stylesheet is read as a file.
    /// </summary>
    public sealed class UiLayoutRulesTests
    {
        private static string stylesheet;

        [OneTimeSetUp]
        public void ReadStylesheet()
        {
            var path = Path.Combine(Application.dataPath, "_Project", "UI", "Resources", "EditorShell.uss");
            Assert.IsTrue(File.Exists(path), path);
            stylesheet = File.ReadAllText(path);
        }

        [TestCase(".dialog")]
        [TestCase(".welcome")]
        [TestCase(".bar-popup")]
        [TestCase(".inspector-frame")]
        [TestCase(".help-menu")]
        [TestCase(".satellite-frame")]
        public void FixedWidthWindowsHaveAMaximumInPercent(string selector)
        {
            var body = Rule(selector);
            Assert.IsTrue(Regex.IsMatch(body, @"(^|[;\s])max-width:\s*\d+(\.\d+)?%"), selector + " needs max-width in percent: " + body);
        }

        [TestCase(".dialog")]
        [TestCase(".menu-popup")]
        [TestCase(".welcome")]
        public void DialogsAndMenusHaveAMaximumHeightInPercent(string selector)
        {
            var body = Rule(selector);
            Assert.IsTrue(Regex.IsMatch(body, @"(^|[;\s])max-height:\s*\d+(\.\d+)?%"), selector + " needs max-height in percent: " + body);
        }

        // M7 step 3 fix: USS gives every element flex-shrink 1, so a clamped dialog squeezed its button row (and title) to nothing and the
        // buttons stood over the dialog's edge. Only the body may give way.
        [TestCase(".dialog-buttons")]
        public void ButtonRowOfADialogDoesNotShrink(string selector)
        {
            Assert.IsTrue(Regex.IsMatch(Rule(selector), @"flex-shrink:\s*0"), selector + " must not shrink: " + Rule(selector));
            Assert.IsTrue(Regex.IsMatch(stylesheet, @"\.dialog-buttons\s*>\s*Button\s*\{[^}]*flex-shrink:\s*0"), "The buttons of the row must not shrink.");
        }

        // M7 step 6 fix: the Map popup opened at the left edge while Chrono, Lines and AI Paths opened at the right one.
        // Objects popup: the two buttons at the left of the bar (tiles, objects) open their lists at the left edge, the others at the right one.
        [Test]
        public void OnlyTheTileAndObjectPopupsOpenAtTheLeftEdge()
        {
            foreach (EditorShellBootstrap.ProjectTab tab in Enum.GetValues(typeof(EditorShellBootstrap.ProjectTab)))
            {
                var atLeft = tab == EditorShellBootstrap.ProjectTab.Tiles || tab == EditorShellBootstrap.ProjectTab.Objects;
                Assert.AreEqual(atLeft, EditorShellBootstrap.BarPopupOnLeft(tab), tab.ToString());
            }
            Assert.IsTrue(Regex.IsMatch(Rule(".bar-popup-right"), @"left:\s*auto") && Regex.IsMatch(Rule(".bar-popup-right"), @"right:\s*var\(--mf-space-2\)"));
        }

        // The object list is a virtual list with a fixed row height: the code's constant is the 28 px item of the list, whose card has the margin above and below (1 px each).
        [Test]
        public void ObjectListRowHeightMatchesTheRowRuleAndTheRowsAreSetInTokens()
        {
            var row = Rule(".objlist-card");
            Assert.IsTrue(Regex.IsMatch(row, @"margin:\s*1px 0"), ".objlist-card needs the 1 px margin of the 28 px row: " + row);
            Assert.IsTrue(Regex.IsMatch(Rule(".bar-popup-wide"), @"width:\s*360px"));
            foreach (Match color in Regex.Matches(Rule(".objlist-card") + Rule(".objlist-chip") + Rule(".objlist-dot-dlc"), @"(?<![\w-])(?:color|background-color|border-color):\s*([^;]+);"))
                StringAssert.Contains("var(--mf-color-", color.Groups[1].Value, "Colours of the object list come from tokens: " + color.Value);
        }

        [TestCase(0, "0")]
        [TestCase(1095, "1095")]
        [TestCase(9999, "9999")]
        [TestCase(10000, "10k")]
        [TestCase(29450, "29k")]
        [TestCase(29500, "30k")]
        [TestCase(99999, "100k")]
        public void TheObjectBadgeShortensFromTenThousand(int count, string expected)
        {
            Assert.AreEqual(expected, EditorShellBootstrap.ObjectBadgeText(count));
        }

        [Test]
        public void TitleOfAFactoryDialogDoesNotShrink()
        {
            Assert.IsTrue(Regex.IsMatch(stylesheet, @"\.dialog-modal \.popup-title,\s*\.dialog-modal \.popup-description\s*\{[^}]*flex-shrink:\s*0"));
        }

        [Test]
        public void BodyOfAFactoryDialogMayShrinkAndStartsAtZero()
        {
            var body = Rule(".dialog-body");
            Assert.IsTrue(Regex.IsMatch(body, @"flex-shrink:\s*[1-9]") && Regex.IsMatch(body, @"min-height:\s*0"), ".dialog-body must give way: " + body);
            Assert.IsTrue(Regex.IsMatch(Rule(".dialog-body-fill"), @"flex-shrink:\s*[1-9]"));
        }

        [Test]
        public void ToolWindowCatalogGivesUpWidthBeforeTheOptionColumn()
        {
            var panel = Regex.Match(Rule(".picker-panel"), @"flex-shrink:\s*(\d+)");
            var options = Regex.Match(Rule(".picker-options"), @"flex-shrink:\s*(\d+)");
            Assert.IsTrue(panel.Success && options.Success);
            Assert.Greater(int.Parse(panel.Groups[1].Value), int.Parse(options.Groups[1].Value) * 100, "The catalog must take nearly all of the missing width.");
            Assert.IsTrue(Regex.IsMatch(Rule(".picker-panel"), @"min-width:\s*(\d+)px") && int.Parse(Regex.Match(Rule(".picker-panel"), @"min-width:\s*(\d+)px").Groups[1].Value) < 700,
                "The catalog's minimum width must be below the 700 px of the fixed layout.");
        }

        // M6 step 4 fix: the road type window and the road picker never overlap, at any interface scale (areas of 900 px logical and wider).
        [TestCase(900f, 506f)]
        [TestCase(900f, 700f)]
        [TestCase(1100f, 700f)]
        [TestCase(1309f, 735f)]
        [TestCase(1600f, 900f)]
        [TestCase(2560f, 1440f)]
        public void RoadWindowStandsRightOfThePickerWithoutCoveringIt(float width, float height)
        {
            const float Margin = 8f;
            var window = OmsiMapEditor.UI.RoadTypeWindow.PreferredSize;
            var picker = OmsiMapEditor.UI.RoadDockRules.PickerWidth(width, window.x, Margin);
            var rect = OmsiMapEditor.UI.RoadDockRules.WindowRect(new Vector2(width, height), picker, window, Margin, 120f);
            Assert.GreaterOrEqual(picker, OmsiMapEditor.UI.RoadDockRules.LeastPickerWidth);
            Assert.GreaterOrEqual(rect.xMin, Margin + picker, "The window starts right of the picker.");
            Assert.LessOrEqual(rect.xMax, width - Margin + 0.01f, "The window ends inside the area.");
            Assert.LessOrEqual(rect.yMax, height - 120f + 0.01f, "The window ends above the bottom bar.");
            Assert.GreaterOrEqual(rect.width, OmsiMapEditor.UI.RoadTypeWindow.LeastSize.x, "The window keeps its least width.");
        }

        // M7 step 7, findings B1 and B2: the vehicle dialog keeps the old spacing (buttons flush left, dense list rows).
        [Test]
        public void TheVehicleDialogKeepsItsOldSpacing()
        {
            Assert.IsTrue(Regex.IsMatch(Rule(".map-control-button.form-action-block"), @"margin-left:\s*0"), "must beat .map-control-button.form-inline-button");
            Assert.IsTrue(Regex.IsMatch(Rule(".form-row-dense"), @"margin:\s*0 0 2px 0"));
            Assert.IsTrue(Regex.IsMatch(Rule(".form-row-dense-3"), @"margin:\s*0 0 3px 0"));
            Assert.IsTrue(stylesheet.IndexOf(".form-row-dense {", StringComparison.Ordinal) > stylesheet.IndexOf(".form-row {", StringComparison.Ordinal), "the dense rule comes after .form-row");
        }

        private static string Rule(string selector)
        {
            var match = Regex.Match(stylesheet, "(^|\n)" + Regex.Escape(selector) + @"\s*\{([^}]*)\}");
            Assert.IsTrue(match.Success, "No rule for " + selector);
            return match.Groups[2].Value;
        }
    }
}
