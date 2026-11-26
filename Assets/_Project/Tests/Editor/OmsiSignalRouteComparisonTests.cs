using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Which signal routes differ from the original map, for the yellow mark in the list (ADR 105, M3 interface).</summary>
    public sealed class OmsiSignalRouteComparisonTests
    {
        private const string Head = "-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\nCreated with Omsi Version 2.2.032\r\nDate: 05.10.2015\r\n\r\n";

        private static string Route(int number, int kind, int signal, int aspect, int element, string optional = "\r\n\r\n\r\n")
        {
            return "   " + number + ":\r\n[signalroute]\r\n" + kind + "\r\n\r\n[signal]\r\n" + signal + "\r\n" + aspect + "\r\n\r\n[entry]\r\n" + element + "\r\n0\r\n0\r\n0\r\n\r\n" + optional;
        }

        private static OmsiSignalRoutes Parse(params string[] routes)
        {
            return OmsiSignalRoutes.Parse(Encoding.ASCII.GetBytes(Head + string.Concat(routes)));
        }

        [Test]
        public void EqualFilesHaveNoEditedRoute()
        {
            var original = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 13, 1, 11));
            CollectionAssert.IsEmpty(OmsiSignalRouteComparison.EditedRoutes(Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 13, 1, 11)), original));
        }

        [Test]
        public void ARouteWithAnotherValueIsEditedAndTheOthersAreNot()
        {
            var original = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 13, 1, 11));
            var current = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 13, 2, 11, "\r\n\r\n[speedlimit]\r\n40.000\r\n\r\n\r\n"));
            CollectionAssert.AreEquivalent(new[] { 1 }, OmsiSignalRouteComparison.EditedRoutes(current, original));
        }

        [Test]
        public void DeletingARouteDoesNotMarkTheRoutesAfterIt()
        {
            var original = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 13, 1, 11), Route(2, 0, 14, 1, 12));
            var current = Parse(Route(0, 0, 13, 1, 11), Route(1, 0, 14, 1, 12));
            CollectionAssert.IsEmpty(OmsiSignalRouteComparison.EditedRoutes(current, original));
        }

        [Test]
        public void ANewPathNumberAloneIsNoEdit()
        {
            var original = Parse(Route(0, 0, 12, 1, 10));
            var current = OmsiSignalRoutes.Parse(Encoding.ASCII.GetBytes(Head + Route(0, 0, 12, 1, 10).Replace("[entry]\r\n10\r\n0\r\n0\r\n0", "[entry]\r\n10\r\n2\r\n0\r\n2")));
            CollectionAssert.IsEmpty(OmsiSignalRouteComparison.EditedRoutes(current, original));
        }

        [Test]
        public void TheCounterpartIsFoundByItsSignalAndTrackNotByItsPlace()
        {
            var original = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 13, 1, 11), Route(2, 0, 14, 1, 12));
            var current = Parse(Route(0, 0, 13, 2, 11, "\r\n\r\n[speedlimit]\r\n40.000\r\n\r\n\r\n"), Route(1, 0, 14, 1, 12));

            var counterpart = OmsiSignalRouteComparison.Counterpart(current.Routes[0], original);

            Assert.IsNotNull(counterpart);
            Assert.AreEqual(1, counterpart.Index);
            Assert.AreEqual(1, counterpart.Aspect);
            Assert.IsNull(counterpart.SpeedLimit);
            Assert.AreEqual(2, OmsiSignalRouteComparison.Counterpart(current.Routes[1], original).Index);
            Assert.IsNull(OmsiSignalRouteComparison.Counterpart(Parse(Route(0, 0, 99, 1, 77)).Routes[0], original));
        }

        [Test]
        public void AnEditStaysWithTheEditedRouteOfTheSameSignalAndTrack()
        {
            var original = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 12, 2, 10));
            // The first route was changed to the aspect of the second; the second is as it was.
            var current = Parse(Route(0, 0, 12, 2, 10), Route(1, 0, 12, 2, 10));
            CollectionAssert.AreEquivalent(new[] { 0 }, OmsiSignalRouteComparison.EditedRoutes(current, original));
        }

        [Test]
        public void ARouteThatMovedWithinItsGroupIsNotEditedButANewValueIs()
        {
            var original = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 12, 2, 10));
            var current = Parse(Route(0, 0, 12, 2, 10), Route(1, 0, 12, 3, 10));
            CollectionAssert.AreEquivalent(new[] { 1 }, OmsiSignalRouteComparison.EditedRoutes(current, original));
        }

        [Test]
        public void TwoRoutesWithSwappedValuesAreNotEditedAsTheirValuesAreAllThere()
        {
            var original = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 12, 2, 10));
            var current = Parse(Route(0, 0, 12, 2, 10), Route(1, 0, 12, 1, 10));
            CollectionAssert.IsEmpty(OmsiSignalRouteComparison.EditedRoutes(current, original));
        }

        [Test]
        public void IdenticalDuplicateRoutesAreMatchedOneForOne()
        {
            var original = Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 12, 1, 10));
            CollectionAssert.IsEmpty(OmsiSignalRouteComparison.EditedRoutes(Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 12, 1, 10)), original));
            CollectionAssert.AreEquivalent(new[] { 2 }, OmsiSignalRouteComparison.EditedRoutes(
                Parse(Route(0, 0, 12, 1, 10), Route(1, 0, 12, 1, 10), Route(2, 0, 12, 1, 10)), original));
            CollectionAssert.IsEmpty(OmsiSignalRouteComparison.EditedRoutes(Parse(Route(0, 0, 12, 1, 10)), original));
        }

        [Test]
        public void WithoutAnOriginalNothingIsMarked()
        {
            CollectionAssert.IsEmpty(OmsiSignalRouteComparison.EditedRoutes(Parse(Route(0, 0, 12, 1, 10)), null));
        }
    }
}
