using NUnit.Framework;
using OmsiMapEditor.UI;
using UnityEngine;

namespace OmsiMapEditor.Tests
{
    /// <summary>The floating inspector sits beside its element and stays inside the free area.</summary>
    public sealed class InspectorPlacementTests
    {
        private static readonly Rect Area = new Rect(0f, 0f, 1000f, 600f);
        private static readonly Vector2 Size = new Vector2(300f, 400f);

        [Test]
        public void SitsBesideTheExtentNotAtItsMiddle()
        {
            // A tile seen from far away: 100 to 400 wide, middle at 250. The window clears the right edge, not the middle.
            var extent = new Rect(100f, 200f, 300f, 250f);
            var corner = InspectorPlacement.Place(extent, extent.center, Size, Area, 28f, 8f);
            Assert.AreEqual(extent.xMax + 28f, corner.x);
            Assert.GreaterOrEqual(corner.y, 8f);
            Assert.LessOrEqual(corner.y + Size.y, 592f);
        }

        [Test]
        public void GoesLeftOfTheExtentWhenTheRightSideIsTooNarrow()
        {
            var extent = new Rect(500f, 200f, 300f, 250f);
            var corner = InspectorPlacement.Place(extent, extent.center, Size, Area, 28f, 8f);
            Assert.AreEqual(extent.xMin - 28f - Size.x, corner.x);
        }

        [Test]
        public void GoesAboveOrBelowTheExtentWhenNeitherSideFits()
        {
            var narrow = new Rect(0f, 0f, 400f, 900f);
            var below = InspectorPlacement.Place(new Rect(40f, 40f, 320f, 40f), new Vector2(200f, 60f), new Vector2(300f, 400f), narrow, 28f, 8f);
            Assert.AreEqual(40f + 40f + 28f, below.y);
            var above = InspectorPlacement.Place(new Rect(40f, 800f, 320f, 40f), new Vector2(200f, 820f), new Vector2(300f, 400f), narrow, 28f, 8f);
            Assert.AreEqual(800f - 28f - 400f, above.y);
        }

        [Test]
        public void AnExtentLargerThanTheFreeAreaFallsBackToTheAnchor()
        {
            // Zoomed in: the tile fills the view, so the window hangs at the anchor exactly as before.
            var extent = new Rect(-2000f, -2000f, 5000f, 5000f);
            var anchor = new Vector2(500f, 300f);
            Assert.AreEqual(InspectorPlacement.Place(anchor, Size, Area, 28f, 8f), InspectorPlacement.Place(extent, anchor, Size, Area, 28f, 8f));
        }

        [Test]
        public void AnExtentWithoutSizeActsLikeThePoint()
        {
            var anchor = new Vector2(200f, 300f);
            Assert.AreEqual(InspectorPlacement.Place(anchor, Size, Area, 28f, 8f), InspectorPlacement.Place(new Rect(anchor, Vector2.zero), anchor, Size, Area, 28f, 8f));
        }

        [Test]
        public void BoundsHoldAllPoints()
        {
            Assert.IsFalse(InspectorPlacement.TryBounds(new Vector2[0], out _));
            Assert.IsTrue(InspectorPlacement.TryBounds(new[] { new Vector2(5f, 9f), new Vector2(-3f, 20f), new Vector2(8f, 1f) }, out var bounds));
            Assert.AreEqual(Rect.MinMaxRect(-3f, 1f, 8f, 20f), bounds);
        }

        [Test]
        public void SitsRightOfTheElementWhenThereIsRoom()
        {
            var corner = InspectorPlacement.Place(new Vector2(200f, 300f), Size, Area, 28f, 8f);
            Assert.AreEqual(228f, corner.x);
            Assert.GreaterOrEqual(corner.y, 8f);
            Assert.LessOrEqual(corner.y + Size.y, 592f);
        }

        [Test]
        public void MovesLeftOfTheElementNearTheRightEdge()
        {
            var corner = InspectorPlacement.Place(new Vector2(900f, 300f), Size, Area, 28f, 8f);
            Assert.AreEqual(900f - 28f - 300f, corner.x);
        }

        [Test]
        public void NeverLeavesTheFreeArea()
        {
            foreach (var anchor in new[] { new Vector2(-50f, -50f), new Vector2(5f, 595f), new Vector2(1200f, 700f), new Vector2(500f, 590f) })
            {
                var corner = InspectorPlacement.Place(anchor, Size, Area, 28f, 8f);
                Assert.GreaterOrEqual(corner.x, 8f);
                Assert.GreaterOrEqual(corner.y, 8f);
                Assert.LessOrEqual(corner.x + Size.x, 992f);
                Assert.LessOrEqual(corner.y + Size.y, 592f);
            }
        }

        [Test]
        public void GoesBelowOrAboveWhenNeitherSideFits()
        {
            var narrow = new Rect(0f, 0f, 340f, 700f);
            var below = InspectorPlacement.Place(new Vector2(170f, 100f), Size, narrow, 28f, 8f);
            Assert.GreaterOrEqual(below.y, 128f);
            var above = InspectorPlacement.Place(new Vector2(170f, 650f), Size, narrow, 28f, 8f);
            Assert.LessOrEqual(above.y + Size.y, 650f - 28f + 0.01f);
        }

        [Test]
        public void TheWindowStaysTransparentThroughAGapInTheDragAndTurnsBackOnce()
        {
            var until = 0f;
            Assert.IsFalse(InspectorPlacement.IsBusy(false, 1f, ref until, 0.12f), "idle");
            Assert.IsTrue(InspectorPlacement.IsBusy(true, 2.00f, ref until, 0.12f), "drag starts");
            // A tick between two pointer events of the same drag does not switch it back.
            Assert.IsTrue(InspectorPlacement.IsBusy(false, 2.05f, ref until, 0.12f), "held");
            Assert.IsTrue(InspectorPlacement.IsBusy(true, 2.08f, ref until, 0.12f), "drag goes on");
            Assert.IsTrue(InspectorPlacement.IsBusy(false, 2.15f, ref until, 0.12f), "held again");
            Assert.IsFalse(InspectorPlacement.IsBusy(false, 2.25f, ref until, 0.12f), "released");
            Assert.IsFalse(InspectorPlacement.IsBusy(false, 2.30f, ref until, 0.12f), "stays released");
        }

        [Test]
        public void TheAnchorFollowsObjectsThenSplineThenRowThenTileAndNothingDocks()
        {
            Assert.AreEqual(InspectorPlacement.Anchor.Objects, InspectorPlacement.ChooseAnchor(3, true, true, true));
            Assert.AreEqual(InspectorPlacement.Anchor.Spline, InspectorPlacement.ChooseAnchor(0, true, true, true));
            Assert.AreEqual(InspectorPlacement.Anchor.Row, InspectorPlacement.ChooseAnchor(0, false, true, true));
            Assert.AreEqual(InspectorPlacement.Anchor.Tile, InspectorPlacement.ChooseAnchor(0, false, false, true));
            Assert.AreEqual(InspectorPlacement.Anchor.None, InspectorPlacement.ChooseAnchor(0, false, false, false), "nothing chosen at all docks at the edge");
        }

        [Test]
        public void ATileMiddleOutsideTheViewKeepsTheWindowAtTheNearestEdge()
        {
            // Zoomed far out or panned away: the middle of the tile lies beside the free area; the window keeps to the edge on that side.
            var left = InspectorPlacement.Place(new Vector2(-4000f, 300f), Size, Area, 28f, 8f);
            Assert.AreEqual(8f + 28f, left.x, 0.01f, "beside the left edge it opens to the right of the clamped anchor");
            var right = InspectorPlacement.Place(new Vector2(5000f, 300f), Size, Area, 28f, 8f);
            Assert.AreEqual(992f - 28f - Size.x, right.x, 0.01f, "beside the right edge it opens to the left of the clamped anchor");
            var below = InspectorPlacement.Place(new Vector2(500f, 9000f), Size, Area, 28f, 8f);
            Assert.LessOrEqual(below.y + Size.y, 592f + 0.01f);
            Assert.GreaterOrEqual(below.y, 8f);
        }

        [Test]
        public void TheLineStartsAtTheNearestPointOfTheWindow()
        {
            var window = new Rect(100f, 100f, 264f, 400f);

            Assert.AreEqual(new Vector2(100f, 150f), InspectorPlacement.NearestPoint(window, new Vector2(60f, 150f)));
            Assert.AreEqual(new Vector2(364f, 500f), InspectorPlacement.NearestPoint(window, new Vector2(400f, 560f)));
            Assert.AreEqual(new Vector2(200f, 300f), InspectorPlacement.NearestPoint(window, new Vector2(200f, 300f)));
        }
    }
}
