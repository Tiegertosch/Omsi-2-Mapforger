using System.Globalization;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Tests
{
    /// <summary>The pure rules of the road type window (what the footer says, when saving is blocked, the actions, widths, labels, keys) and its frame built without a panel.</summary>
    public sealed class RoadTypeWindowRulesTests
    {
        // ----- the footer and saving -----

        [Test]
        public void TheFooterNamesTheMostImportantStateFirst()
        {
            Assert.AreEqual(RoadFooterState.Stale, RoadTypeWindowRules.Footer(false, true, true, 2));
            Assert.AreEqual(RoadFooterState.Invalid, RoadTypeWindowRules.Footer(false, true, false, 1));
            Assert.AreEqual(RoadFooterState.Changed, RoadTypeWindowRules.Footer(false, true, false, 0));
            Assert.AreEqual(RoadFooterState.NewUnsaved, RoadTypeWindowRules.Footer(true, false, false, 0));
            Assert.AreEqual(RoadFooterState.Unchanged, RoadTypeWindowRules.Footer(false, false, false, 0));
        }

        [Test]
        public void EveryFooterStateHasItsOwnKey()
        {
            var keys = System.Enum.GetValues(typeof(RoadFooterState)).Cast<RoadFooterState>().Select(RoadTypeWindowRules.FooterKey).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
        }

        [Test]
        public void ANewRoadTypeIsSavedWithoutChangesButAnExistingOneNeedsThem()
        {
            Assert.AreEqual(RoadSaveBlock.None, RoadTypeWindowRules.SaveBlock(true, false, false, 0, true, false));
            Assert.AreEqual(RoadSaveBlock.NoChanges, RoadTypeWindowRules.SaveBlock(false, false, false, 0, true, true));
            Assert.AreEqual(RoadSaveBlock.None, RoadTypeWindowRules.SaveBlock(false, true, false, 0, true, true));
        }

        [Test]
        public void SavingIsBlockedWithItsReason()
        {
            Assert.AreEqual(RoadSaveBlock.ReadOnly, RoadTypeWindowRules.SaveBlock(true, true, false, 0, false, true));
            Assert.AreEqual(RoadSaveBlock.Problems, RoadTypeWindowRules.SaveBlock(true, true, false, 1, true, true));
            Assert.AreEqual(RoadSaveBlock.Stale, RoadTypeWindowRules.SaveBlock(false, true, true, 0, true, true));
            Assert.AreEqual(RoadSaveBlock.NotOwned, RoadTypeWindowRules.SaveBlock(false, true, false, 0, true, false), "a road type of another project may only be saved as a new type");
            Assert.IsNull(RoadTypeWindowRules.SaveBlockKey(RoadSaveBlock.None));
            foreach (var block in new[] { RoadSaveBlock.ReadOnly, RoadSaveBlock.NoChanges, RoadSaveBlock.Problems, RoadSaveBlock.Stale, RoadSaveBlock.NotOwned })
                Assert.IsNotNull(RoadTypeWindowRules.SaveBlockKey(block), block.ToString());
        }

        // ----- the actions under the preview -----

        [Test]
        public void TheArrowsStopAtTheEnds()
        {
            var first = RoadTypeWindowRules.Actions(4, 0, true);
            Assert.IsFalse(first.CanMoveLeft);
            Assert.IsTrue(first.CanMoveRight);
            var last = RoadTypeWindowRules.Actions(4, 3, true);
            Assert.IsTrue(last.CanMoveLeft);
            Assert.IsFalse(last.CanMoveRight);
            var only = RoadTypeWindowRules.Actions(1, 0, true);
            Assert.IsFalse(only.CanMoveLeft || only.CanMoveRight);
            Assert.IsTrue(only.CanRemove, "the last element may go, the empty draft just cannot be saved");
        }

        [Test]
        public void WithoutASelectedElementNothingActsOnIt()
        {
            var none = RoadTypeWindowRules.Actions(3, -1, false);
            Assert.IsFalse(none.CanMoveLeft || none.CanMoveRight || none.CanDuplicate || none.CanRemove);
            Assert.IsTrue(none.CanAdd);
            var boundary = RoadTypeWindowRules.Actions(3, 1, false);
            Assert.IsFalse(boundary.CanRemove, "a selected line is no element");
        }

        [Test]
        public void AFullRoadTypeTakesNoMoreElementsAndSaysWhy()
        {
            var full = RoadTypeWindowRules.Actions(OmsiRoadType.MaximumLanes, 2, true);
            Assert.IsFalse(full.CanAdd);
            Assert.IsFalse(full.CanDuplicate);
            Assert.IsNotNull(full.AddBlockedKey);
            Assert.IsTrue(full.CanRemove);
        }

        // ----- widths -----

        [TestCase("3.25", 3.25)]
        [TestCase("3,25", 3.25)]
        [TestCase(" 2,5 m", 2.5)]
        [TestCase("3.254", 3.25)]
        [TestCase("3.255", 3.26)]
        public void AWidthIsReadWithACommaOrAPointAndRoundedToCentimetres(string text, double expected)
        {
            Assert.IsTrue(RoadTypeWindowRules.TryParseWidth(text, out var metres));
            Assert.AreEqual(expected, metres, 1e-9);
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("-1")]
        [TestCase("0")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("0.001")]
        public void TextThatIsNoWidthIsRefused(string text)
        {
            Assert.IsFalse(RoadTypeWindowRules.TryParseWidth(text, out _));
        }

        [Test]
        public void AWidthIsShownWithTheDecimalMarkOfTheCulture()
        {
            Assert.AreEqual("3,25", RoadTypeWindowRules.FormatWidth(3.25, new CultureInfo("de-DE")));
            Assert.AreEqual("3.25", RoadTypeWindowRules.FormatWidth(3.25, CultureInfo.InvariantCulture));
        }

        // ----- the preview -----

        [Test]
        public void DrawnWidthsAreProportionalWithoutAHiddenMinimum()
        {
            var drawn = RoadTypeWindowRules.DrawnWidths(new[] { 3.0, 3.0, 6.0 }, 120f);
            Assert.AreEqual(new[] { 30f, 30f, 60f }, drawn);
            Assert.AreEqual(0, RoadTypeWindowRules.DrawnWidths(new double[0], 100f).Length);
            Assert.AreEqual(new[] { 0f }, RoadTypeWindowRules.DrawnWidths(new[] { 0.0 }, 100f));
        }

        [Test]
        public void ANarrowElementKeepsAClickableWidthWhileItsDrawingStaysTrue()
        {
            Assert.AreEqual(14f, RoadTypeWindowRules.HitWidth(3f));
            Assert.AreEqual(80f, RoadTypeWindowRules.HitWidth(80f));
        }

        [Test]
        public void LabelsThinOutBeforeTheyWouldOverlap()
        {
            Assert.AreEqual(RoadLabelLevel.Full, RoadTypeWindowRules.LabelLevel(120f, 4, 4));
            Assert.AreEqual(RoadLabelLevel.Full, RoadTypeWindowRules.LabelLevel(40f, 4, 4));
            Assert.AreEqual(RoadLabelLevel.Number, RoadTypeWindowRules.LabelLevel(20f, 4, 4));
            Assert.AreEqual(RoadLabelLevel.None, RoadTypeWindowRules.LabelLevel(5f, 4, 4));
            // A longer width text asks for more room than the short mark alone.
            Assert.AreEqual(RoadLabelLevel.Short, RoadTypeWindowRules.LabelLevel(40f, 4, 9));
        }

        // ----- problems, names, keys -----

        [Test]
        public void EveryProblemCodeHasAKey()
        {
            var keys = System.Enum.GetValues(typeof(OmsiRoadTypeProblemCode)).Cast<OmsiRoadTypeProblemCode>().Select(RoadTypeWindowRules.ProblemKey).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
        }

        [Test]
        public void EveryKindAndEveryTemplateHasAKey()
        {
            foreach (OmsiRoadLaneKind kind in System.Enum.GetValues(typeof(OmsiRoadLaneKind)))
            {
                StringAssert.StartsWith("roadbuilder.panel.lane.", RoadTypeWindowRules.LaneKey(kind));
                StringAssert.EndsWith(".short", RoadTypeWindowRules.ShortKey(kind));
            }
            var keys = OmsiRoadTypeTemplate.All.Select(template => RoadTypeWindowRules.TemplateKey(template.Id)).ToList();
            Assert.AreEqual(OmsiRoadTypeTemplate.All.Count, keys.Distinct().Count(), "each template has a name of its own");
        }

        // ----- keys -----

        [Test]
        public void CtrlZUndoesAndCtrlYOrCtrlShiftZRedo()
        {
            Assert.AreEqual(RoadWindowKeyAction.Undo, RoadTypeWindowRules.KeyAction(KeyCode.Z, true, false));
            Assert.AreEqual(RoadWindowKeyAction.Redo, RoadTypeWindowRules.KeyAction(KeyCode.Y, true, false));
            Assert.AreEqual(RoadWindowKeyAction.Redo, RoadTypeWindowRules.KeyAction(KeyCode.Z, true, true));
            Assert.AreEqual(RoadWindowKeyAction.None, RoadTypeWindowRules.KeyAction(KeyCode.Z, false, false), "Z alone is no tool key here either");
            Assert.AreEqual(RoadWindowKeyAction.None, RoadTypeWindowRules.KeyAction(KeyCode.Q, true, false));
        }

        [Test]
        public void EveryKeyButEscapeStaysAwayFromTheMapShortcuts()
        {
            foreach (var key in new[] { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.Delete, KeyCode.Z, KeyCode.Y, KeyCode.Backspace })
                Assert.IsTrue(RoadTypeWindowRules.SwallowsShortcut(key), key.ToString());
            Assert.IsFalse(RoadTypeWindowRules.SwallowsShortcut(KeyCode.Escape), "Esc closes the window through the shell, with its question");
        }

        // ----- the cross-section strip -----

        [Test]
        public void AStripThatFitsIsDrawnAtTheFittedScaleWithTrueProportions()
        {
            var widths = new[] { 3.0, 3.25, 3.25, 3.0 };
            var scale = RoadTypeWindowRules.StripScale(widths, 500f);
            Assert.AreEqual(500f / 12.5f, scale, 0.001f);
            var edges = RoadTypeWindowRules.StripEdges(widths, scale);
            Assert.AreEqual(5, edges.Length);
            Assert.AreEqual(0f, edges[0]);
            Assert.AreEqual(500f, edges[4], 1f, "the elements fill the room without a gap");
            Assert.AreEqual((edges[2] - edges[1]) / (edges[1] - edges[0]), 3.25f / 3.0f, 0.03f, "the proportions are the widths'");
        }

        [Test]
        public void ManyNarrowElementsZoomTheWholeStripInsteadOfDistortingIt()
        {
            var widths = new double[16];
            for (var index = 0; index < widths.Length; index++)
                widths[index] = 0.5;
            var fitted = 300f / 8f;
            var scale = RoadTypeWindowRules.StripScale(widths, 300f);
            Assert.Greater(scale, fitted, "zoomed in");
            Assert.LessOrEqual(scale, fitted * RoadTypeWindowRules.MostZoom + 0.001f);
            var edges = RoadTypeWindowRules.StripEdges(widths, scale);
            Assert.GreaterOrEqual(edges[1] - edges[0], RoadTypeWindowRules.LeastElementPixels - 1f, "the narrowest element can be hit");
            Assert.Greater(edges[16], 300f, "the strip scrolls sideways");
            for (var index = 1; index < edges.Length; index++)
                Assert.AreEqual(edges[1] - edges[0], edges[index] - edges[index - 1], 1.01f, "all share one scale");
        }

        [Test]
        public void TheZoomHasALimitSoAnExtremeRoadTypeDoesNotBecomeHuge()
        {
            var widths = new[] { 0.05, 40.0 };
            var scale = RoadTypeWindowRules.StripScale(widths, 400f);
            Assert.AreEqual(400f / 40.05f * RoadTypeWindowRules.MostZoom, scale, 0.01f);
            Assert.AreEqual(0f, RoadTypeWindowRules.StripScale(new double[0], 400f));
            Assert.AreEqual(0f, RoadTypeWindowRules.StripScale(widths, 0f));
        }

        [Test]
        public void ABoundaryCanAlwaysBePickedAndTheElementsInBetweenToo()
        {
            var edges = new[] { 0f, 100f, 124f, 224f };
            Assert.AreEqual(0, RoadTypeWindowRules.HitStrip(edges, 50f).Index);
            Assert.IsFalse(RoadTypeWindowRules.HitStrip(edges, 50f).Boundary);
            var near = RoadTypeWindowRules.HitStrip(edges, 102f);
            Assert.IsTrue(near.Boundary);
            Assert.AreEqual(0, near.Index, "the boundary is named by the element left of it");
            var narrow = RoadTypeWindowRules.HitStrip(edges, 112f);
            Assert.IsFalse(narrow.Boundary, "the 24 px element has a middle that is not a boundary");
            Assert.AreEqual(1, narrow.Index);
            var second = RoadTypeWindowRules.HitStrip(edges, 122f);
            Assert.IsTrue(second.Boundary);
            Assert.AreEqual(1, second.Index);
            Assert.IsFalse(RoadTypeWindowRules.HitStrip(edges, 1f).Boundary, "the outer edges are no boundaries");
            Assert.IsFalse(RoadTypeWindowRules.HitStrip(edges, 223f).Boundary);
            Assert.AreEqual(2, RoadTypeWindowRules.HitStrip(edges, 400f).Index, "beyond the end the last element");
            Assert.AreEqual(-1, RoadTypeWindowRules.HitStrip(new[] { 0f }, 5f).Index, "no elements, no hit");
        }

        [Test]
        public void ADraggedElementIsDroppedIntoTheGapNearestItsCentre()
        {
            var edges = new[] { 0f, 100f, 200f, 300f };
            Assert.AreEqual(0, RoadTypeWindowRules.DropGap(edges, 10f));
            Assert.AreEqual(1, RoadTypeWindowRules.DropGap(edges, 120f));
            Assert.AreEqual(3, RoadTypeWindowRules.DropGap(edges, 290f));
            Assert.AreEqual(2, RoadTypeWindowRules.DropIndex(0, 3), "dropped after the last it takes the last place");
            Assert.AreEqual(0, RoadTypeWindowRules.DropIndex(2, 0));
            Assert.AreEqual(1, RoadTypeWindowRules.DropIndex(1, 1), "into its own gap it stays");
            Assert.AreEqual(1, RoadTypeWindowRules.DropIndex(1, 2), "and into the gap after itself");
        }

        [Test]
        public void TheSelectionIsScrolledIntoViewWithTheLeastMovement()
        {
            Assert.AreEqual(50f, RoadTypeWindowRules.ScrollToReveal(100f, 140f, 50f, 300f), "in view already: no movement");
            Assert.AreEqual(88f, RoadTypeWindowRules.ScrollToReveal(100f, 140f, 200f, 300f), "left of the view");
            Assert.AreEqual(272f, RoadTypeWindowRules.ScrollToReveal(500f, 560f, 0f, 300f), "right of the view");
            Assert.AreEqual(0f, RoadTypeWindowRules.ScrollToReveal(5f, 20f, 40f, 300f), "never before the start");
        }

        [Test]
        public void ANumberTypedInAFieldIsReadWithCommaOrPointAndNotRounded()
        {
            Assert.IsTrue(RoadTypeWindowRules.TryParseNumber(" 12,5 ", out var comma));
            Assert.AreEqual(12.5, comma);
            Assert.IsTrue(RoadTypeWindowRules.TryParseNumber("-45.25", out var negative));
            Assert.AreEqual(-45.25, negative);
            Assert.IsTrue(RoadTypeWindowRules.TryParseNumber("0", out var zero));
            Assert.AreEqual(0.0, zero);
            foreach (var bad in new[] { "", " ", "abc", "1,2,3", "NaN", "Infinity", "1e999" })
                Assert.IsFalse(RoadTypeWindowRules.TryParseNumber(bad, out _), bad);
        }

        [Test]
        public void AnObjectChosenForAMedianAndItsMiddlePlaceAreOneUndoStep()
        {
            var draft = new OmsiRoadTypeDraft(OmsiRoadType.Parse("median 2 | drive 3.25"));
            var id = draft.IdAt(0);
            draft.SetRowPlace(id, OmsiRoadRowPlace.Middle, "row-object");
            draft.SetRowObject(id, "Sceneryobjects\\Test\\tree.sco", 90.0, "row-object");
            draft.EndGesture();
            Assert.AreEqual(OmsiRoadRowPlace.Middle, draft.LaneOf(id).RowPlace);
            Assert.AreEqual(90.0, draft.LaneOf(id).RowTurn);
            Assert.IsTrue(draft.Undo());
            Assert.IsNull(draft.LaneOf(id).RowObject, "one undo takes the object away");
            Assert.AreEqual(OmsiRoadRowPlace.Kerb, draft.LaneOf(id).RowPlace, "and the place with it");
            Assert.IsFalse(draft.Undo(), "no second step");
        }

        [Test]
        public void TheWindowTakesTheFocusBackOnlyWhenItHadItAndLostIt()
        {
            // (had it, has any, in the window, reachable)
            Assert.IsTrue(RoadTypeWindowRules.NeedsFocusBack(true, false, false, false), "the focused element was removed");
            Assert.IsTrue(RoadTypeWindowRules.NeedsFocusBack(true, true, false, true), "the focus went elsewhere by itself");
            Assert.IsTrue(RoadTypeWindowRules.NeedsFocusBack(true, true, true, false), "the clicked button was disabled or hidden");
            Assert.IsFalse(RoadTypeWindowRules.NeedsFocusBack(true, true, true, true), "nothing lost");
            Assert.IsFalse(RoadTypeWindowRules.NeedsFocusBack(false, false, false, false), "drawing never takes the focus from the map");
            Assert.IsFalse(RoadTypeWindowRules.NeedsFocusBack(false, true, false, true));
        }

        // ----- the window's frame, shared with the signals window -----

        [Test]
        public void TheFrameOfTheSignalsWindowKeepsItsClassesAndRightAlignedButtons()
        {
            var window = new SignalWindow(() => { }, _ => { }, _ => { });
            Assert.IsTrue(window.ClassListContains("floating-panel"));
            Assert.IsTrue(window.ClassListContains("signal-frame"));
            Assert.IsNotNull(window.Header.Q<Button>(className: "signal-collapse-button"), "the collapse button that pushes the header buttons to the right edge");
            Assert.AreEqual(2, window.Header.Query<Button>(className: "panel-collapse-button").ToList().Count, "collapse and close");
            var content = window.Content.Children().ToList();
            Assert.AreSame(window.Footer, content.Last(), "the footer is last");
            Assert.AreEqual(content.Count - 2, content.IndexOf(window.Content.Q(className: "signal-prompt")), "the question stands above the footer");
            Assert.IsNotNull(window.Grip);
        }
    }
}
