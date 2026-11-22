using System.Collections.Generic;
using NUnit.Framework;
using OmsiMapEditor.UI;
using UnityEngine;

namespace OmsiMapEditor.Tests
{
    /// <summary>Where an object has to stand to be seen above a tool window (ADR 098, M4): it moves only when it is under the window or too close to its edge.</summary>
    public sealed class FreeAreaPanTests
    {
        private const float Top = 36f;
        private const float Window = 590f;
        private const float Margin = 90f;

        [Test]
        public void ObjectInTheFreePartStaysWhereItIs()
        {
            Assert.AreEqual(0f, FreeAreaPan.ShiftY(300f, Top, Window, Margin));
            Assert.AreEqual(0f, FreeAreaPan.ShiftY(Top + Margin, Top, Window, Margin), "exactly at the margin is clear");
            Assert.AreEqual(0f, FreeAreaPan.ShiftY(Window - Margin, Top, Window, Margin));
        }

        [Test]
        public void ObjectUnderTheWindowMovesToTheMiddleOfTheFreePart()
        {
            var middle = (Top + Window) * 0.5f;
            Assert.AreEqual(middle - 700f, FreeAreaPan.ShiftY(700f, Top, Window, Margin), 0.001f);
            Assert.AreEqual(middle - 560f, FreeAreaPan.ShiftY(560f, Top, Window, Margin), 0.001f, "too close to the window");
        }

        [Test]
        public void ObjectNearTheTopEdgeMovesDown()
        {
            Assert.Greater(FreeAreaPan.ShiftY(Top + 20f, Top, Window, Margin), 0f);
        }

        [Test]
        public void AFreePartTooSmallToKeepAnythingClearMovesNothing()
        {
            Assert.AreEqual(0f, FreeAreaPan.ShiftY(700f, Top, Top + Margin * 2f, Margin));
            Assert.AreEqual(0f, FreeAreaPan.ShiftY(700f, float.NaN, Window, Margin), "a window not laid out yet");
            Assert.AreEqual(0f, FreeAreaPan.ShiftY(float.NaN, Top, Window, Margin));
        }

        // The windows of a tool: the options column on the left and the catalog on the right with a gap between, both with their top at 590.
        private static readonly IReadOnlyList<Rect> Windows = new[] { new Rect(300f, Window, 280f, 300f), new Rect(600f, Window, 700f, 300f) };

        [Test]
        public void PointAboveTheWindowsStaysWhereItIs()
        {
            Assert.AreEqual(0f, FreeAreaPan.ShiftFor(400f, 300f, Windows, Top, Margin));
            Assert.AreEqual(0f, FreeAreaPan.ShiftFor(400f, Window - Margin, Windows, Top, Margin), "exactly at the margin is clear");
        }

        [Test]
        public void PointUnderOrNearTheWindowsMovesToTheMiddleOfTheFreePart()
        {
            var middle = (Top + Window) * 0.5f;
            Assert.AreEqual(middle - 700f, FreeAreaPan.ShiftFor(400f, 700f, Windows, Top, Margin), 0.001f, "under the window");
            Assert.AreEqual(middle - 560f, FreeAreaPan.ShiftFor(900f, 560f, Windows, Top, Margin), 0.001f, "within the margin above it");
            Assert.AreEqual(middle - 580f, FreeAreaPan.ShiftFor(240f, 580f, Windows, Top, Margin), 0.001f, "beside it, within the margin");
        }

        [Test]
        public void PointFarToTheSideOfTheWindowsStaysWhereItIs()
        {
            Assert.AreEqual(0f, FreeAreaPan.ShiftFor(100f, 700f, Windows, Top, Margin), "left of the window");
            Assert.AreEqual(0f, FreeAreaPan.ShiftFor(1500f, 700f, Windows, Top, Margin), "right of the window");
        }

        [Test]
        public void ThePointIsKeptClearOfTheHighestWindowItIsNear()
        {
            var windows = new[] { new Rect(300f, 590f, 280f, 300f), new Rect(600f, 400f, 700f, 490f) };
            Assert.AreEqual((Top + 400f) * 0.5f - 500f, FreeAreaPan.ShiftFor(900f, 500f, windows, Top, Margin), 0.001f, "under the taller catalog");
            Assert.AreEqual((Top + 590f) * 0.5f - 700f, FreeAreaPan.ShiftFor(400f, 700f, windows, Top, Margin), 0.001f, "under the shorter column only");
        }

        [Test]
        public void WindowsNotLaidOutYetOrNoneMoveNothing()
        {
            Assert.AreEqual(0f, FreeAreaPan.ShiftFor(400f, 700f, new Rect[0], Top, Margin));
            Assert.AreEqual(0f, FreeAreaPan.ShiftFor(400f, 700f, null, Top, Margin));
            Assert.AreEqual(0f, FreeAreaPan.ShiftFor(400f, 700f, new[] { new Rect(300f, float.NaN, 280f, 0f), new Rect(0f, 0f, 0f, 0f) }, Top, Margin));
            Assert.AreEqual(0f, FreeAreaPan.ShiftFor(float.NaN, 700f, Windows, Top, Margin));
        }

        [Test]
        public void TheFreePartEndsAtTheHighestWindow()
        {
            Assert.AreEqual(400f, FreeAreaPan.FreeBottom(new[] { new Rect(300f, 590f, 280f, 300f), new Rect(600f, 400f, 700f, 490f) }));
            Assert.IsNaN(FreeAreaPan.FreeBottom(new Rect[0]));
            Assert.IsNaN(FreeAreaPan.FreeBottom(null));
            Assert.IsNaN(FreeAreaPan.FreeBottom(new[] { new Rect(0f, 0f, 0f, 0f) }), "a window without a size is not laid out");
        }

        [Test]
        public void ARequestWaitsWhileThePointerHoldsSomethingAndForTheWindowsPlace()
        {
            Assert.AreEqual(FreeAreaStep.Wait, FreeAreaRequest.Next(false, true, false, false, true), "dragging at the first look");
            Assert.AreEqual(FreeAreaStep.Wait, FreeAreaRequest.Next(false, false, false, false, false), "the window has no place yet");
            Assert.AreEqual(FreeAreaStep.Pan, FreeAreaRequest.Next(false, false, false, false, true), "a click that moved nothing pans once let go");
        }

        [Test]
        public void ARequestIsDroppedWhenReplacedWhenTheViewChangedOrWhenTheTargetWasMoved()
        {
            Assert.AreEqual(FreeAreaStep.Drop, FreeAreaRequest.Next(true, false, false, false, true), "a newer request, or the tool was left");
            Assert.AreEqual(FreeAreaStep.Drop, FreeAreaRequest.Next(true, true, false, false, true), "even while held");
            Assert.AreEqual(FreeAreaStep.Drop, FreeAreaRequest.Next(false, false, true, false, true), "the user moved the view");
            Assert.AreEqual(FreeAreaStep.Drop, FreeAreaRequest.Next(false, false, false, true, true), "the user dragged the object near the window");
            Assert.AreEqual(FreeAreaStep.Wait, FreeAreaRequest.Next(false, true, true, true, true), "judged only once the pointer lets go");
        }

        [Test]
        public void ATargetCountsAsMovedAfterMoreThanAHundredthOfAMetre()
        {
            Assert.IsFalse(FreeAreaRequest.Moved(10.0, 20.0, 10.005, 20.0));
            Assert.IsTrue(FreeAreaRequest.Moved(10.0, 20.0, 10.0, 20.5));
            Assert.IsTrue(FreeAreaRequest.Moved(10.0, 20.0, 12.0, 20.0));
        }
    }
}
