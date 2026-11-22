using NUnit.Framework;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>M6 step 3: the arithmetic of the interface scale (Windows dpi, "Interface size", window limit).</summary>
    public sealed class EditorShellScaleTests
    {
        [Test]
        public void DpiZeroFallsBackTo96()
        {
            Assert.AreEqual(96f, EditorShellScale.EffectiveDpi(0f));
            Assert.AreEqual(96f, EditorShellScale.EffectiveDpi(-5f));
            Assert.AreEqual(96f, EditorShellScale.EffectiveDpi(float.NaN));
            var result = EditorShellScale.Calculate(0f, 100, 1f, 1920f, null);
            Assert.AreEqual(1f, result.Effective, 0.0001f);
            Assert.IsFalse(result.Clamped);
        }

        [Test]
        public void WindowsScaleTimesUserFactorTimesDefault()
        {
            Assert.AreEqual(1.5f, EditorShellScale.Calculate(144f, 100, 1f, 3000f, null).Effective, 0.0001f);
            Assert.AreEqual(1.5f * 1.2f, EditorShellScale.Calculate(144f, 120, 1f, 3000f, null).Effective, 0.0001f);
            Assert.AreEqual(1.25f * 0.9f * 1.1f, EditorShellScale.Calculate(120f, 90, 1.1f, 3000f, null).Effective, 0.0001f);
        }

        [Test]
        public void UserPercentStaysBetween90And200InStepsOfTen()
        {
            Assert.AreEqual(90, EditorShellScale.NormalizePercent(10));
            Assert.AreEqual(90, EditorShellScale.NormalizePercent(-40));
            Assert.AreEqual(200, EditorShellScale.NormalizePercent(900));
            Assert.AreEqual(100, EditorShellScale.NormalizePercent(100));
            Assert.AreEqual(130, EditorShellScale.NormalizePercent(127));
            Assert.AreEqual(2f, EditorShellScale.Calculate(96f, 9999, 1f, 5000f, null).UserFactor, 0.0001f);
            Assert.AreEqual(0.9f, EditorShellScale.Calculate(96f, 0, 1f, 5000f, null).UserFactor, 0.0001f);
        }

        [Test]
        public void LogicalWidthNeverFallsBelow900()
        {
            // 1100 px wide at 200 %: 1100 / 900 = 1.222; the wish stays 2.
            var result = EditorShellScale.Calculate(96f, 200, 1f, 1100f, null);
            Assert.AreEqual(2f, result.Wanted, 0.0001f);
            Assert.AreEqual(1100f / 900f, result.Effective, 0.0001f);
            Assert.IsTrue(result.Clamped);
            Assert.GreaterOrEqual(1100f / result.Effective, 900f - 0.01f);
        }

        [Test]
        public void ALargeWindowIsNotClamped()
        {
            var result = EditorShellScale.Calculate(96f, 200, 1f, 1800f, null);
            Assert.AreEqual(2f, result.Effective, 0.0001f);
            Assert.IsFalse(result.Clamped);
        }

        [Test]
        public void AWindowNarrowerThan900IsNotShrunkBelow100Percent()
        {
            var result = EditorShellScale.Calculate(96f, 150, 1f, 800f, null);
            Assert.AreEqual(1f, result.Effective, 0.0001f);
            Assert.AreEqual(1f, EditorShellScale.Calculate(96f, 100, 1f, 800f, null).Effective, 0.0001f);
            Assert.AreEqual(0.9f, EditorShellScale.Calculate(96f, 90, 1f, 800f, null).Effective, 0.0001f);
        }

        [Test]
        public void ArgumentReplacesTheProductButStaysLimited()
        {
            Assert.AreEqual(1.5f, EditorShellScale.Calculate(144f, 200, 1f, 3000f, 1.5f).Effective, 0.0001f);
            var limited = EditorShellScale.Calculate(96f, 100, 1f, 1100f, 2f);
            Assert.AreEqual(2f, limited.Wanted, 0.0001f);
            Assert.AreEqual(1100f / 900f, limited.Effective, 0.0001f);
        }

        [Test]
        public void UnknownWindowWidthDoesNotClamp()
        {
            Assert.AreEqual(2f, EditorShellScale.Calculate(96f, 200, 1f, 0f, null).Effective, 0.0001f);
        }

        [Test]
        public void ArgumentIsParsedInRangeOnly()
        {
            Assert.AreEqual(1.5f, EditorShellScale.ArgumentFactor(new[] { "x", "-uiScale", "1.5" }));
            Assert.IsNull(EditorShellScale.ArgumentFactor(new[] { "-uiScale", "9" }));
            Assert.IsNull(EditorShellScale.ArgumentFactor(new[] { "-uiScale" }));
            Assert.IsNull(EditorShellScale.ArgumentFactor(null));
        }

        [Test]
        public void LogLineNamesDpiUserFactorAndClamp()
        {
            var result = EditorShellScale.Calculate(96f, 200, 1f, 1100f, 2f);
            var line = EditorShellScale.Describe(result, true, 1100f);
            StringAssert.Contains("dpi=96", line);
            StringAssert.Contains("user=2", line);
            StringAssert.Contains("scale=1.222", line);
            StringAssert.Contains("clamped from 2", line);
        }

        [Test]
        public void ScaleIsCalculatedAgainWhenWidthOrDpiChanges()
        {
            Assert.IsFalse(EditorShellScale.NeedsRecalculation(1600f, 96f, 1600f, 96f));
            Assert.IsTrue(EditorShellScale.NeedsRecalculation(1600f, 96f, 1500f, 96f));
            Assert.IsTrue(EditorShellScale.NeedsRecalculation(1600f, 96f, 1600f, 144f), "a window moved to a screen with another dpi");
        }

        [Test]
        public void MenuWaitsForItsWidthAndStaysInsideTheWindow()
        {
            Assert.IsNull(EditorShellBootstrap.MenuLeft(100f, 1000f, float.NaN));
            Assert.AreEqual(100f, EditorShellBootstrap.MenuLeft(100f, 1000f, 200f).Value, 0.001f);
            Assert.AreEqual(796f, EditorShellBootstrap.MenuLeft(900f, 1000f, 200f).Value, 0.001f);
            Assert.AreEqual(4f, EditorShellBootstrap.MenuLeft(-20f, 1000f, 200f).Value, 0.001f);
            Assert.AreEqual(4f, EditorShellBootstrap.MenuLeft(300f, 150f, 200f).Value, 0.001f, "a menu wider than the window keeps its left margin");
            Assert.AreEqual(796f, EditorShellBootstrap.MenuLeft(796f, 1000f, 200f).Value, 0.001f, "exactly at the right margin");
            Assert.IsNull(EditorShellBootstrap.MenuLeft(float.NaN, 1000f, 200f));
            Assert.IsNull(EditorShellBootstrap.MenuLeft(100f, float.NaN, 200f));
        }

        [Test]
        public void HeldBackReportWaitsForTheRestOfTheViewSentence()
        {
            Assert.AreEqual(2050L, EditorShellBootstrap.DeferredReportDelayMs(10f, 8f));
            Assert.AreEqual(50L, EditorShellBootstrap.DeferredReportDelayMs(10f, 12f), "an elapsed time does not give a negative delay");
        }
    }
}
