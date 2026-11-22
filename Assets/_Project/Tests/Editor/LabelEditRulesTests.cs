using NUnit.Framework;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>When the label lines in the inspector are written (ADR 060, M4 "Labels"): <see cref="LabelEditRules"/>.</summary>
    public sealed class LabelEditRulesTests
    {
        [Test]
        public void TypedLinesAreWrittenWhenTheMapIsEditable()
        {
            Assert.That(LabelEditRules.Decide(true, true, true, false), Is.EqualTo(LabelEditRules.FlushAction.Write));
        }

        [Test]
        public void TypedLinesAreCarriedWhenTheMapCannotBeEditedNow()
        {
            Assert.That(LabelEditRules.Decide(true, true, false, false), Is.EqualTo(LabelEditRules.FlushAction.Carry));
        }

        [Test]
        public void TypedLinesAreDiscardedNotWrittenWhenTheMapIsReadAfterAFailedRollBack()
        {
            Assert.That(LabelEditRules.Decide(true, true, true, true), Is.EqualTo(LabelEditRules.FlushAction.Discard));
            Assert.That(LabelEditRules.Decide(true, true, false, true), Is.EqualTo(LabelEditRules.FlushAction.Discard));
        }

        [Test]
        public void UnchangedOrForeignLinesAreDroppedWhateverTheMode()
        {
            Assert.That(LabelEditRules.Decide(true, false, true, true), Is.EqualTo(LabelEditRules.FlushAction.Drop));
            Assert.That(LabelEditRules.Decide(false, true, true, false), Is.EqualTo(LabelEditRules.FlushAction.Drop));
            Assert.That(LabelEditRules.Decide(false, true, true, true), Is.EqualTo(LabelEditRules.FlushAction.Drop));
        }

        [Test]
        public void LinesOfTheSameMapVariantAndElementStillApply()
        {
            var map = new object();
            var variant = new object();

            Assert.That(LabelEditRules.StillApplies(map, variant, map, variant, true), Is.True);
            Assert.That(LabelEditRules.StillApplies(map, null, map, null, true), Is.True);
        }

        [Test]
        public void LinesOfAnotherMapAreNotWritten()
        {
            Assert.That(LabelEditRules.StillApplies(new object(), null, new object(), null, true), Is.False);
            Assert.That(LabelEditRules.StillApplies(new object(), null, null, null, true), Is.False);
            Assert.That(LabelEditRules.StillApplies(null, null, null, null, true), Is.False);
        }

        [Test]
        public void LinesOfAnotherChronoVariantAreNotWritten()
        {
            var map = new object();

            Assert.That(LabelEditRules.StillApplies(map, null, map, new object(), true), Is.False);
            Assert.That(LabelEditRules.StillApplies(map, new object(), map, null, true), Is.False);
            Assert.That(LabelEditRules.StillApplies(map, new object(), map, new object(), true), Is.False);
        }

        [Test]
        public void LinesOfAnElementThatLeftItsTileAreNotWritten()
        {
            var map = new object();

            Assert.That(LabelEditRules.StillApplies(map, null, map, null, false), Is.False);
        }

        [Test]
        public void UnchangedLinesWriteNothing()
        {
            var written = new[] { "Einsteindorf", "Linie 7" };

            Assert.That(LabelEditRules.Differs(written, new[] { "Einsteindorf", "Linie 7" }), Is.False);
            Assert.That(LabelEditRules.WritesOnEnter(written, new[] { "Einsteindorf", "Linie 7" }), Is.False);
            Assert.That(LabelEditRules.WritesOnFocusLoss(false, written, new[] { "Einsteindorf", "Linie 7" }), Is.False);
        }

        [Test]
        public void OneChangedLineWritesAllLinesTogether()
        {
            var written = new[] { "Einsteindorf", "Linie 7" };

            Assert.That(LabelEditRules.Differs(written, new[] { "Einsteindorf", "Linie 8" }), Is.True);
            Assert.That(LabelEditRules.WritesOnEnter(written, new[] { "Einsteindorf", "Linie 8" }), Is.True);
            Assert.That(LabelEditRules.WritesOnFocusLoss(false, written, new[] { "Einsteindorf", "Linie 8" }), Is.True);
        }

        [Test]
        public void MovingToAnotherLineOfTheSameElementStillWaits()
        {
            Assert.That(LabelEditRules.WritesOnFocusLoss(true, new[] { "a", "b" }, new[] { "changed", "b" }), Is.False);
        }

        [Test]
        public void CaseAndSpacesCountAsChanges()
        {
            Assert.That(LabelEditRules.Differs(new[] { "Mitte" }, new[] { "mitte" }), Is.True);
            Assert.That(LabelEditRules.Differs(new[] { "Mitte" }, new[] { "Mitte " }), Is.True);
        }

        [Test]
        public void AMissingLineCountsAsEmptyAndANullLineToo()
        {
            Assert.That(LabelEditRules.Differs(new[] { "a", string.Empty }, new[] { "a" }), Is.False);
            Assert.That(LabelEditRules.Differs(new[] { "a" }, new[] { "a", string.Empty }), Is.False);
            Assert.That(LabelEditRules.Differs(new[] { "a", null }, new[] { "a", string.Empty }), Is.False);
            Assert.That(LabelEditRules.Differs(new[] { "a" }, new[] { "a", "b" }), Is.True);
            Assert.That(LabelEditRules.Differs(null, null), Is.False);
        }
    
        [Test]
        public void ARenamedFirstLabelRenamesTheListedStopOnlyUnderItsOldName()
        {
            // Changed, and the list still has the old name: renamed.
            Assert.That(LabelEditRules.RenamesListedStop("Old", "New", "Old"), Is.True);
            Assert.That(LabelEditRules.RenamesListedStop(" Old ", "New ", "Old"), Is.True);
            // Label 0 not changed (another label was edited): the list is left alone, whatever it says.
            Assert.That(LabelEditRules.RenamesListedStop("Old", "Old", "Other"), Is.False);
            Assert.That(LabelEditRules.RenamesListedStop("Old", " Old ", "Old"), Is.False);
            // The list was set on purpose to another name: not overwritten.
            Assert.That(LabelEditRules.RenamesListedStop("Old", "New", "Chosen"), Is.False);
            // Not listed, or no new name.
            Assert.That(LabelEditRules.RenamesListedStop("Old", "New", null), Is.False);
            Assert.That(LabelEditRules.RenamesListedStop("Old", " ", "Old"), Is.False);
            Assert.That(LabelEditRules.RenamesListedStop(null, "New", string.Empty), Is.True);
            Assert.That(LabelEditRules.CanAddToStopList("Stop", false), Is.True);
            Assert.That(LabelEditRules.CanAddToStopList("  ", false), Is.False);
            Assert.That(LabelEditRules.CanAddToStopList(null, false), Is.False);
            Assert.That(LabelEditRules.CanAddToStopList("Stop", true), Is.False);
        }

        [Test]
        public void RemovalFromTheStopListNeedsAListedStopThatNoTripUses()
        {
            Assert.That(LabelEditRules.CanRemoveFromStopList(true, new string[0]), Is.True);
            Assert.That(LabelEditRules.CanRemoveFromStopList(true, new[] { "T1" }), Is.False);
            Assert.That(LabelEditRules.CanRemoveFromStopList(false, new string[0]), Is.False);
            Assert.That(LabelEditRules.CanRemoveFromStopList(true, null), Is.False);
        }

        [Test]
        public void TheTooltipNamesAFewTripsAndCountsTheRest()
        {
            var shown = LabelEditRules.TripsToShow(new[] { "A", "B", "C", "D", "E" }, 3, out var more);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, shown);
            Assert.That(more, Is.EqualTo(2));
            shown = LabelEditRules.TripsToShow(new[] { "A" }, 3, out more);
            CollectionAssert.AreEqual(new[] { "A" }, shown);
            Assert.That(more, Is.EqualTo(0));
            Assert.That(LabelEditRules.TripsToShow(null, 3, out more), Is.Empty);
            Assert.That(more, Is.EqualTo(0));
        }
    }
}
