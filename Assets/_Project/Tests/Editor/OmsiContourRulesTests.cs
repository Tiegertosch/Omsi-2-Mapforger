using System;
using System.Collections.Generic;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiContourRulesTests
    {
        private static OmsiTerrain Terrain(Func<int, int, float> height)
        {
            var heights = new float[OmsiTerrain.Resolution * OmsiTerrain.Resolution];
            for (var row = 0; row < OmsiTerrain.Resolution; row++)
                for (var column = 0; column < OmsiTerrain.Resolution; column++)
                    heights[row * OmsiTerrain.Resolution + column] = height(column, row);
            return OmsiTerrain.Parse(OmsiTerrain.Encode(heights));
        }

        [Test]
        public void TheRangeSpansAllTilesAndSkipsNotANumber()
        {
            var low = Terrain((column, row) => row == 3 && column == 4 ? -12.5f : 0f);
            var high = Terrain((column, row) => row == 0 && column == 0 ? 74f : float.NaN);
            Assert.IsTrue(OmsiContourRules.TryGetRange(new[] { low, null, high }, out var minimum, out var maximum));
            Assert.AreEqual(-12.5, minimum, 1e-6);
            Assert.AreEqual(74.0, maximum, 1e-6);
            Assert.IsFalse(OmsiContourRules.IsFlat(minimum, maximum));
        }

        [Test]
        public void NoTerrainAndOnlyNotANumberGiveNoRange()
        {
            Assert.IsFalse(OmsiContourRules.TryGetRange(null, out var minimum, out var maximum));
            Assert.AreEqual(0, minimum);
            Assert.AreEqual(0, maximum);
            Assert.IsFalse(OmsiContourRules.TryGetRange(new OmsiTerrain[0], out _, out _));
            Assert.IsFalse(OmsiContourRules.TryGetRange(new[] { Terrain((c, r) => float.NaN) }, out _, out _));
        }

        [Test]
        public void AMapOfOneHeightIsFlatEvenBelowSeaLevel()
        {
            Assert.IsTrue(OmsiContourRules.TryGetRange(new[] { Terrain((c, r) => 0f), Terrain((c, r) => 0f) }, out var minimum, out var maximum));
            Assert.IsTrue(OmsiContourRules.IsFlat(minimum, maximum));
            Assert.IsTrue(OmsiContourRules.IsFlat(-3.0, -3.0));
            Assert.IsTrue(OmsiContourRules.IsFlat(0, OmsiContourRules.FlatSpanMeters * 0.99));
            Assert.IsFalse(OmsiContourRules.IsFlat(0, OmsiContourRules.FlatSpanMeters));
            Assert.IsTrue(OmsiContourRules.IsFlat(double.NaN, 1));
        }

        [Test]
        public void AManualSpacingIsKeptWithinTheLimits()
        {
            Assert.AreEqual(0.25, OmsiContourRules.ClampInterval(0.01), 1e-9);
            Assert.AreEqual(0.25, OmsiContourRules.ClampInterval(-4), 1e-9);
            Assert.AreEqual(10.0, OmsiContourRules.ClampInterval(50), 1e-9);
            Assert.AreEqual(2.5, OmsiContourRules.ClampInterval(2.5), 1e-9);
            Assert.AreEqual(OmsiContourRules.DefaultInterval, OmsiContourRules.ClampInterval(double.NaN), 1e-9);
            Assert.AreEqual(OmsiContourRules.DefaultInterval, OmsiContourRules.ClampInterval(double.PositiveInfinity), 1e-9);
        }

        [Test]
        public void AutomaticSpacingUsesNiceStepsOnly()
        {
            var nice = new List<double>(OmsiContourRules.NiceIntervals);
            foreach (var span in new[] { 0.5, 14.8, 86.4, 400.0 })
                foreach (var metersPerPixel in new[] { 0.05, 0.25, 1.0, 2.5, 4.0, 20.0, 200.0 })
                    Assert.Contains(OmsiContourRules.PickInterval(metersPerPixel, span), nice, "span " + span + ", " + metersPerPixel + " m/px");
        }

        [Test]
        public void AutomaticSpacingGrowsWhenTheViewZoomsOut()
        {
            var previous = 0.0;
            foreach (var metersPerPixel in new[] { 0.05, 0.25, 1.0, 4.0, 16.0, 64.0 })
            {
                var interval = OmsiContourRules.PickInterval(metersPerPixel, 86.4);
                Assert.GreaterOrEqual(interval, previous, metersPerPixel + " m/px");
                previous = interval;
            }
        }

        [Test]
        public void AGrundorfLikeMapGetsFineSpacingAndSpandauCoarserAtTheSameZoom()
        {
            // Grundorf: 0 to 15 m; Spandau: -12.5 to 74 m. Close zoom: 1 m per pixel.
            var grundorf = OmsiContourRules.PickInterval(1.0, 15.0);
            var spandau = OmsiContourRules.PickInterval(1.0, 86.5);
            Assert.LessOrEqual(grundorf, 1.0);
            Assert.GreaterOrEqual(spandau, grundorf);
            // Zoomed far out on Grundorf the spacing may not exceed a quarter of the range.
            Assert.LessOrEqual(OmsiContourRules.PickInterval(200.0, 15.0), 2.5);
            Assert.AreEqual(0.25, OmsiContourRules.PickInterval(0.01, 15.0), 1e-9);
        }

        [Test]
        public void ASteepMeanSlopeAsksForCoarserLinesAtTheSameZoom()
        {
            Assert.Greater(OmsiContourRules.PickInterval(2.0, 86.0, 0.2), OmsiContourRules.PickInterval(2.0, 86.0, 0.01));
        }

        [Test]
        public void BadInputFallsBackToTheDefaultSpacing()
        {
            Assert.AreEqual(OmsiContourRules.DefaultInterval, OmsiContourRules.PickInterval(0, 50));
            Assert.AreEqual(OmsiContourRules.DefaultInterval, OmsiContourRules.PickInterval(-1, 50));
            Assert.AreEqual(OmsiContourRules.DefaultInterval, OmsiContourRules.PickInterval(double.NaN, 50));
            Assert.AreEqual(OmsiContourRules.DefaultInterval, OmsiContourRules.PickInterval(1, double.NaN));
            Assert.AreEqual(OmsiContourRules.DefaultInterval, OmsiContourRules.PickInterval(1, 0));
        }

        [Test]
        public void EveryFifthLineIsMajorAlsoBelowZero()
        {
            Assert.IsTrue(OmsiContourRules.IsMajorLine(0));
            Assert.IsTrue(OmsiContourRules.IsMajorLine(5));
            Assert.IsTrue(OmsiContourRules.IsMajorLine(-10));
            Assert.IsFalse(OmsiContourRules.IsMajorLine(1));
            Assert.IsFalse(OmsiContourRules.IsMajorLine(-1));
            Assert.IsFalse(OmsiContourRules.IsMajorLine(4));
        }

        [Test]
        public void SettingsStartOffAutomaticWithoutBandAndClampTheManualValue()
        {
            var settings = new OmsiContourSettings();
            Assert.IsFalse(settings.Enabled);
            Assert.IsTrue(settings.Automatic);
            Assert.IsFalse(settings.ShowBand);
            settings.ManualInterval = 99;
            Assert.AreEqual(10.0, settings.ManualInterval, 1e-9);
            settings.Automatic = false;
            Assert.AreEqual(10.0, settings.EffectiveInterval(1, 50), 1e-9);
            settings.Automatic = true;
            Assert.AreEqual(OmsiContourRules.PickInterval(1, 50), settings.EffectiveInterval(1, 50), 1e-9);
        }
    }
}
