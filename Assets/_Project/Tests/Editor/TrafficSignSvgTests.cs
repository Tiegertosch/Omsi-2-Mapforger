using System;
using System.Collections.Generic;
using NUnit.Framework;
using OmsiMapEditor.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The SVG signs the owner draws (ADR 099): what the reader takes, what it turns down and how a missing or broken file falls back.</summary>
    public sealed class TrafficSignSvgTests
    {
        private const string Open = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\">";

        private Func<string, string> savedSource;

        [SetUp]
        public void Remember()
        {
            savedSource = TrafficSignSvgs.Source;
            TrafficSignSvgs.Reset();
        }

        [TearDown]
        public void Restore()
        {
            TrafficSignSvgs.Source = savedSource;
            TrafficSignSvgs.Reset();
        }

        private static SvgSign Parse(string body)
        {
            Assert.IsTrue(SvgSign.TryParse(Open + body + "</svg>", out var sign, out var error), error);
            return sign;
        }

        private static string Rejected(string text)
        {
            Assert.IsFalse(SvgSign.TryParse(text, out var sign, out var error), "should be rejected: " + text);
            Assert.IsNull(sign);
            Assert.IsNotEmpty(error);
            return error;
        }

        [Test]
        public void EveryShapeOfTheSubsetIsRead()
        {
            var sign = Parse("<path d=\"M1 1h4v4z\" fill=\"#ff0000\"/><circle cx=\"10\" cy=\"10\" r=\"4\"/><rect x=\"2\" y=\"2\" width=\"3\" height=\"3\"/><polygon points=\"0,0 5,0 5,5\" stroke=\"black\" stroke-width=\"2\"/>");

            Assert.AreEqual(4, sign.ShapeCount);
            Assert.AreEqual(new Rect(0f, 0f, 20f, 20f), sign.ViewBox);
        }

        [Test]
        public void TheTransformsOfGroupsAreAppliedToTheShapesAndTheLeftOneIsOutermost()
        {
            // The circle (centre 10,10, radius 5) is scaled by 2 and then moved by 10 across: centre 30,20 radius 10.
            var sign = Parse("<g transform=\"translate(10 0) scale(2)\"><circle cx=\"10\" cy=\"10\" r=\"5\"/></g>");

            var bounds = sign.Bounds;
            Assert.AreEqual(20f, bounds.xMin, 1e-3f);
            Assert.AreEqual(40f, bounds.xMax, 1e-3f);
            Assert.AreEqual(10f, bounds.yMin, 1e-3f);
            Assert.AreEqual(30f, bounds.yMax, 1e-3f);

            // The other way round the scale is outermost and the shift is doubled: x from 30 to 50.
            var other = Parse("<g transform=\"scale(2) translate(10 0)\"><circle cx=\"10\" cy=\"10\" r=\"5\"/></g>");
            Assert.AreEqual(30f, other.Bounds.xMin, 1e-3f);
            Assert.AreEqual(50f, other.Bounds.xMax, 1e-3f);
        }

        [Test]
        public void StyleBeatsAttributesAndGroupsPassTheirPaintOn()
        {
            Assert.IsTrue(SvgSign.TryParse(Open + "<g fill=\"#00f\" stroke=\"rgb(1,2,3)\" stroke-width=\"2px\"><path d=\"M0 0h5v5z\" style=\"fill:none;stroke-linejoin:round\"/></g></svg>", out _, out var error), error);
            Assert.IsTrue(SvgSign.TryParse(Open + "<path d=\"M0 0h5v5z\" fill=\"currentColor\" fill-rule=\"evenodd\" fill-opacity=\"1\"/></svg>", out _, out error), error);
        }

        [Test]
        public void TitlesEmptyDefsAndElementsOfOtherNamespacesAreSkipped()
        {
            var sign = Parse("<title>x</title><defs/><sodipodi:namedview xmlns:sodipodi=\"http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd\"/><path d=\"M0 0h5v5z\"/>");

            Assert.AreEqual(1, sign.ShapeCount);
        }

        [Test]
        public void WhatIsOutsideTheSubsetMakesTheWholeFileInvalid()
        {
            StringAssert.Contains("viewBox", Rejected("<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0h5v5z\"/></svg>"));
            StringAssert.Contains("text", Rejected(Open + "<text>50</text></svg>"));
            StringAssert.Contains("<ellipse>", Rejected(Open + "<ellipse cx=\"1\" cy=\"1\" rx=\"1\" ry=\"2\"/></svg>"));
            StringAssert.Contains("gradient", Rejected(Open + "<path d=\"M0 0h5v5z\" fill=\"url(#g)\"/></svg>"));
            StringAssert.Contains("rotate", Rejected(Open + "<g transform=\"rotate(45)\"><path d=\"M0 0h5v5z\"/></g></svg>"));
            StringAssert.Contains("matrix", Rejected(Open + "<g transform=\"matrix(1 0 0 1 5 5)\"><path d=\"M0 0h5v5z\"/></g></svg>"));
            StringAssert.Contains("<defs>", Rejected(Open + "<defs><path id=\"a\" d=\"M0 0h5v5z\"/></defs><path d=\"M0 0h5v5z\"/></svg>"));
            StringAssert.Contains("selector", Rejected(Open + "<style>path {fill:red}</style><path d=\"M0 0h5v5z\"/></svg>"));
            StringAssert.Contains("selector", Rejected(Open + "<style>.a .b {fill:red}</style><path d=\"M0 0h5v5z\"/></svg>"));
            StringAssert.Contains("opacity", Rejected(Open + "<path d=\"M0 0h5v5z\" opacity=\"0.5\"/></svg>"));
            StringAssert.Contains("rounded", Rejected(Open + "<rect width=\"4\" height=\"4\" rx=\"1\"/></svg>"));
            StringAssert.Contains("nothing", Rejected(Open + "</svg>"));
            Rejected(Open + "<path d=\"M0 0 Q\"/></svg>");
            Rejected(Open + "<path d=\"M0 0h5v5z\" fill=\"#12\"/></svg>");
        }

        [Test]
        public void NoXmlAnEmptyFileOrAnotherRootAreRejectedAndADocTypeIsNotRead()
        {
            Rejected("");
            Rejected("not xml");
            Rejected("<html/>");
        }

        [Test]
        public void ADocTypeIsIgnoredAndNeverFetched()
        {
            // CorelDRAW and Illustrator write the SVG 1.1 DOCTYPE; it is skipped, nothing is loaded from the address.
            const string doctype = "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://localhost:1/svg11.dtd\">";
            Assert.IsTrue(SvgSign.TryParse("<?xml version=\"1.0\"?>" + doctype + Open + "<path d=\"M0 0h5v5z\"/></svg>", out var sign, out var error), error);
            Assert.AreEqual(1, sign.ShapeCount);
        }

        [Test]
        public void ClassRulesOfAStyleBlockApplyBelowTheStyleAttributeAsCorelDrawWritesThem()
        {
            var sign = Parse("<defs><style type=\"text/css\"><![CDATA[ .fil1 {fill:#CE1719} .fil0 {fill:#2B2A29;fill-rule:nonzero} /* c */ ]]></style></defs>" +
                             "<path class=\"fil0\" d=\"M0 0h5v5z\"/><path class=\"fil1 other\" d=\"M5 5h5v5z\" style=\"fill:none\"/>");

            Assert.AreEqual(2, sign.ShapeCount);
            // An unknown class name is harmless; an unreadable value inside a rule still invalidates the file.
            StringAssert.Contains("colour", Rejected(Open + "<style>.a{fill:url(#g)}</style><path class=\"a\" d=\"M0 0h5v5z\"/></svg>"));
        }

        [Test]
        public void ABrokenFileIsReportedOnceAndTheNameFallsBack()
        {
            var reads = 0;
            TrafficSignSvgs.Source = name =>
            {
                reads++;
                return name == "yield" ? "<svg" : null;
            };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Traffic sign yield.svg is not used"));
            Assert.IsNull(TrafficSignSvgs.Get("yield"));
            Assert.IsNull(TrafficSignSvgs.Get("yield"));
            Assert.IsNull(TrafficSignSvgs.Get("priority-road"));
            Assert.IsNull(TrafficSignSvgs.Get("priority-road"));
            // Each name is looked up once, hits and misses alike: a redraw of hundreds of signs asks the files nothing.
            Assert.AreEqual(2, reads);
        }

        [Test]
        public void AFileThatCannotBeReadFallsBackToo()
        {
            TrafficSignSvgs.Source = _ => throw new InvalidOperationException("locked");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("could not be read: locked"));
            Assert.IsNull(TrafficSignSvgs.Get("yield"));
        }

        [Test]
        public void AValidFileIsFoundByItsName()
        {
            var files = new Dictionary<string, string> { ["speed-50"] = Open + "<circle cx=\"10\" cy=\"10\" r=\"9\"/></svg>" };
            TrafficSignSvgs.Source = name => files.TryGetValue(name, out var text) ? text : null;

            Assert.IsNotNull(TrafficSignSvgs.Get(TrafficSignSvgs.SpeedName(50)));
            Assert.IsNull(TrafficSignSvgs.Get(TrafficSignSvgs.SpeedName(30)));
            Assert.AreEqual("speed-100", TrafficSignSvgs.SpeedName(100));
        }

        private static List<PathStep> Steps(string data)
        {
            var steps = new List<PathStep>();
            SvgPath.Parse(data, steps);
            return steps;
        }

        private static void AssertSameSteps(string expected, string actual)
        {
            var a = Steps(expected);
            var b = Steps(actual);
            Assert.AreEqual(a.Count, b.Count, actual);
            for (var index = 0; index < a.Count; index++)
            {
                Assert.AreEqual(a[index].Kind, b[index].Kind, actual);
                Assert.AreEqual(a[index].A, b[index].A, actual);
                Assert.AreEqual(a[index].B, b[index].B, actual);
                Assert.AreEqual(a[index].C, b[index].C, actual);
            }
        }

        [Test]
        public void CompactArcFlagsAreReadOneCharacterEach()
        {
            AssertSameSteps("M0 0a5 5 0 0 1 1 1", "M0 0a5 5 0 011 1");
            AssertSameSteps("M0 0a5 5 0 1 0 2 2", "M0 0a5 5 0 102 2");
            AssertSameSteps("M0 0a5 5 0 1 1 -2 2", "M0 0a5,5,0,1,1,-2,2");
            // Two arcs in a row with compact flags, as minifiers write them.
            AssertSameSteps("M0 0a2 2 0 0 1 2 2a2 2 0 0 0 2 2", "M0 0a2 2 0 012 2a2 2 0 002 2");
            Assert.Throws<FormatException>(() => Steps("M0 0a5 5 0 2 1 1 1"));
        }

        [Test]
        public void EveryLucideIconStillReadsAsAWhole()
        {
            var paths = 0;
            foreach (var icon in LucideIcons.Paths)
                foreach (var data in icon.Value)
                {
                    var steps = new List<PathStep>();
                    Assert.DoesNotThrow(() => SvgPath.Parse(data, steps), icon.Key + ": " + data);
                    Assert.Greater(steps.Count, 0, icon.Key);
                    foreach (var step in steps)
                        foreach (var point in new[] { step.A, step.B, step.C })
                            Assert.IsTrue(!float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.y), icon.Key);
                    paths++;
                }
            Assert.Greater(paths, 100);
        }

        [Test]
        public void PathDataWithStrayCharactersMissingNumbersOrHugeNumbersIsInvalid()
        {
            StringAssert.Contains("X", Rejected(Open + "<path d=\"M 0 0 X 5 5\"/></svg>"));
            StringAssert.Contains("range", Rejected(Open + "<path d=\"M 0 0 L 1e39 5\"/></svg>"));
            Rejected(Open + "<path d=\"M 0 0 L 5 #\"/></svg>");
            Rejected(Open + "<path d=\"M 0 0 L 5\"/></svg>");
            Rejected(Open + "<path d=\"M 0 0 h 5 z 5\"/></svg>");
            Rejected(Open + "<path d=\"M 0 0 L Infinity 5\"/></svg>");
        }

        [Test]
        public void ADocTypeWithAnInternalEntityFailsCleanlyWithoutExpandingIt()
        {
            const string text = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY big \"M0 0h5v5z\">]>" + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\"><path d=\"&big;\"/></svg>";
            Assert.IsFalse(SvgSign.TryParse(text, out var sign, out var error));
            Assert.IsNull(sign);
            Assert.IsNotEmpty(error);

            // Nothing expands, so a file that only declares an entity and does not use it is as good as one without a DOCTYPE.
            const string unused = "<!DOCTYPE svg [<!ENTITY big \"x\">]><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\"><path d=\"M0 0h5v5z\"/></svg>";
            Assert.IsTrue(SvgSign.TryParse(unused, out sign, out error), error);
            Assert.AreEqual(1, sign.ShapeCount);
        }

        [Test]
        public void ASpeedSignWithAMissingDigitFallsBackAsAWhole()
        {
            var files = new Dictionary<string, string>
            {
                ["speed-blank"] = Open + "<circle cx=\"10\" cy=\"10\" r=\"9\"/></svg>",
                ["digit-5"] = Open + "<rect width=\"4\" height=\"6\"/></svg>"
            };
            TrafficSignSvgs.Source = name => files.TryGetValue(name, out var text) ? text : null;
            TrafficSignSvgs.Reset();

            // 55 has its digits, 50 lacks the 0, 9 lacks the 9: the drawing must say so before it draws anything, so the built-in sign is not covered by a ring alone.
            using (var painter = new Painter2D())
            {
                var centre = new Vector2(50f, 50f);
                Assert.IsTrue(TrafficSignSvgs.TryDraw(painter, TrafficSignKind.Speed, 55, centre, 40f));
                Assert.IsFalse(TrafficSignSvgs.TryDraw(painter, TrafficSignKind.Speed, 50, centre, 40f));
                Assert.IsFalse(TrafficSignSvgs.TryDraw(painter, TrafficSignKind.Speed, 9, centre, 40f));
                Assert.IsFalse(TrafficSignSvgs.TryDraw(painter, TrafficSignKind.Speed, 1000, centre, 40f));
            }
        }

        [Test]
        public void SwitchingTheArmedTileToASignLetsAClickGoToAnArmNotToARoadPiece()
        {
            var options = new TrafficToolOptions();
            options.Arm(new TrafficItem { Kind = TrafficItemKind.OvertakingBan });
            Assert.IsTrue(options.Armed.Value.OnRoad);
            Assert.AreEqual(TrafficTab.Rules, options.Tab);

            options.Arm(new TrafficItem { Kind = TrafficItemKind.RightOfWay, Sign = OmsiRightOfWaySign.PriorityRoad });
            Assert.IsFalse(options.Armed.Value.OnRoad);
            Assert.AreEqual(TrafficItemKind.RightOfWay, options.Armed.Value.Kind);

            // A tile of the other tab is let go of on the switch, so no stale road tile stays armed.
            options.Arm(new TrafficItem { Kind = TrafficItemKind.OvertakingBan });
            options.Open(TrafficTab.Speed);
            Assert.IsFalse(options.Armed.HasValue);
        }

        [Test]
        public void TheShippedSignFilesAllRead()
        {
            // The files of Resources/TrafficSigns as the player loads them: each must read as a whole, with shapes and a viewBox.
            foreach (var name in new[] { "priority-road", "yield", "overtaking-ban", "speed-blank", "tab-rules", "tab-speed", "digit-0", "digit-1", "digit-2", "digit-3", "digit-4", "digit-5", "digit-6", "digit-7", "digit-8", "digit-9" })
            {
                var asset = Resources.Load<TextAsset>(TrafficSignSvgs.Folder + name + ".svg");
                Assert.IsNotNull(asset, name);
                Assert.IsTrue(SvgSign.TryParse(asset.text, out var sign, out var error), name + ": " + error);
                Assert.Greater(sign.ShapeCount, 0, name);
                Assert.Greater(sign.Bounds.width, 0f, name);
            }
        }
    }
}
