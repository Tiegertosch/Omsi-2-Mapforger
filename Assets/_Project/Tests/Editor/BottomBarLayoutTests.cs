using NUnit.Framework;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The distance of the windows from the bottom of the area follows the measured height of the bottom bar.</summary>
    public sealed class BottomBarLayoutTests
    {
        [Test]
        public void DistanceIsTheMeasuredHeightPlusTheGap()
        {
            Assert.AreEqual(106f, BottomBarLayout.DistanceAbove(94f, BottomBarLayout.PopupGap));
            Assert.AreEqual(104f, BottomBarLayout.DistanceAbove(94f, BottomBarLayout.ToolGap));
            Assert.AreEqual(46f, BottomBarLayout.DistanceAbove(34f, BottomBarLayout.PopupGap));
        }

        [Test]
        public void AnUnmeasuredBarCountsAsZeroHeight()
        {
            Assert.AreEqual(0f, BottomBarLayout.CleanHeight(float.NaN));
            Assert.AreEqual(0f, BottomBarLayout.CleanHeight(-5f));
            Assert.AreEqual(BottomBarLayout.ToolGap, BottomBarLayout.DistanceAbove(float.NaN, BottomBarLayout.ToolGap));
        }
    }
}
