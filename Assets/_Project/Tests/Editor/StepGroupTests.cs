using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Undoing or redoing the parts of a grouped step all or none (ADR 098, M3).</summary>
    public sealed class StepGroupTests
    {
        /// <summary>A part of a group: whether it is in effect, and how it fails.</summary>
        private sealed class Part
        {
            public string Name;
            public bool Applied;
            /// <summary>Fails before changing anything, as a refused step or a write put back.</summary>
            public bool FailsBefore;
            /// <summary>Changes whether it is in effect and then fails, as a step whose terrain fit or drawing fails after its files were written.</summary>
            public bool FailsAfter;
            public bool TakeBackFails;
        }

        private readonly List<string> calls = new List<string>();
        private readonly List<OmsiRollBackFailedException> noted = new List<OmsiRollBackFailedException>();

        // NUnit keeps one instance for all tests of the class.
        [SetUp]
        public void Clear()
        {
            calls.Clear();
            noted.Clear();
        }

        private void Apply(Part part)
        {
            calls.Add("apply " + part.Name);
            if (part.FailsBefore)
                throw new IOException(part.Name + " failed.");
            part.Applied = !part.Applied;
            if (part.FailsAfter)
                throw new IOException(part.Name + " failed after writing.");
        }

        private void TakeBack(Part part)
        {
            calls.Add("take back " + part.Name);
            if (part.TakeBackFails)
                throw new IOException(part.Name + " cannot be taken back.");
            part.Applied = !part.Applied;
        }

        private void Run(params Part[] parts)
        {
            StepGroup.Apply(parts, Apply, TakeBack, part => new List<bool> { part.Applied }, noted.Add);
        }

        [Test]
        public void EveryPartIsAppliedInOrder()
        {
            var parts = new[] { new Part { Name = "a" }, new Part { Name = "b" } };

            Run(parts);

            CollectionAssert.AreEqual(new[] { "apply a", "apply b" }, calls);
            Assert.IsTrue(parts.All(part => part.Applied));
            Assert.IsEmpty(noted);
        }

        [Test]
        public void APartThatFailsBeforeChangingAnythingTakesBackOnlyThePartsDoneBefore()
        {
            var parts = new[] { new Part { Name = "a" }, new Part { Name = "b" }, new Part { Name = "c", FailsBefore = true } };

            var failure = Assert.Throws<IOException>(() => Run(parts));

            Assert.AreEqual("c failed.", failure.Message, "The failure is thrown as it was.");
            CollectionAssert.AreEqual(new[] { "apply a", "apply b", "apply c", "take back b", "take back a" }, calls);
            Assert.IsTrue(parts.All(part => !part.Applied));
            Assert.IsEmpty(noted);
        }

        [Test]
        public void APartThatFailsAfterChangingItsStateIsTakenBackFirst()
        {
            var parts = new[] { new Part { Name = "a" }, new Part { Name = "b", FailsAfter = true }, new Part { Name = "c" } };

            var failure = Assert.Throws<IOException>(() => Run(parts));

            Assert.AreEqual("b failed after writing.", failure.Message);
            CollectionAssert.AreEqual(new[] { "apply a", "apply b", "take back b", "take back a" }, calls);
            // Every part is as before the call, so the history can put the step back on its stack.
            Assert.IsTrue(parts.All(part => !part.Applied));
            Assert.IsEmpty(noted);
        }

        [Test]
        public void TakingBackTheFailedPartThatFailsReportsBothOnce()
        {
            var parts = new[] { new Part { Name = "a" }, new Part { Name = "b", FailsAfter = true, TakeBackFails = true } };

            var failed = Assert.Throws<OmsiRollBackFailedException>(() => Run(parts));

            Assert.AreEqual(1, noted.Count);
            Assert.AreSame(failed, noted[0]);
            var both = (AggregateException)failed.InnerException;
            Assert.AreEqual("b failed after writing.", both.InnerExceptions[0].Message);
            Assert.AreEqual("b cannot be taken back.", both.InnerExceptions[1].Message);
            StringAssert.StartsWith("b failed after writing.", failed.Message);
            CollectionAssert.AreEqual(new[] { "apply a", "apply b", "take back b" }, calls);
        }

        [Test]
        public void APartDoneBeforeThatCannotBeTakenBackReportsBothOnce()
        {
            var parts = new[] { new Part { Name = "a", TakeBackFails = true }, new Part { Name = "b", FailsBefore = true } };

            var failed = Assert.Throws<OmsiRollBackFailedException>(() => Run(parts));

            Assert.AreEqual(1, noted.Count);
            Assert.AreSame(failed, noted[0]);
            CollectionAssert.AreEqual(new[] { "apply a", "apply b", "take back a" }, calls);
        }

        [Test]
        public void AFailedRollBackOfAPartIsThrownOnWithoutTakingBackTheOthers()
        {
            var parts = new[] { new Part { Name = "a" } };
            var inner = new OmsiRollBackFailedException("a and its roll-back failed.", new AggregateException(new IOException("a"), new IOException("roll-back")));

            var failed = Assert.Throws<OmsiRollBackFailedException>(() =>
                StepGroup.Apply(new[] { new Part { Name = "first" }, parts[0] }, part =>
                {
                    calls.Add("apply " + part.Name);
                    if (part == parts[0])
                        throw inner;
                    part.Applied = !part.Applied;
                }, TakeBack, part => new List<bool> { part.Applied }, noted.Add));

            Assert.AreSame(inner, failed, "The workspace may hold part of the step, so the map is read again; nothing is taken back.");
            CollectionAssert.AreEqual(new[] { "apply first", "apply a" }, calls);
            Assert.IsEmpty(noted, "The session reported it where it happened.");
        }

        [Test]
        public void APartWithoutAStateIsNotTakenBackWhenItFails()
        {
            var parts = new[] { new Part { Name = "a" }, new Part { Name = "b", FailsAfter = true } };

            Assert.Throws<IOException>(() => StepGroup.Apply(parts, Apply, TakeBack, part => new List<bool>(), noted.Add));

            CollectionAssert.AreEqual(new[] { "apply a", "apply b", "take back a" }, calls);
        }
    }
}
