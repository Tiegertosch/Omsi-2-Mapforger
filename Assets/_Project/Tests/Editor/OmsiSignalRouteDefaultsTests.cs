using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>What a new signal route starts with (ADR 105, M4 interface).</summary>
    public sealed class OmsiSignalRouteDefaultsTests
    {
        private static string Route(int number, int kind, int signal, string optional)
        {
            return "   " + number + ":\r\n[signalroute]\r\n" + kind + "\r\n\r\n[signal]\r\n" + signal + "\r\n1\r\n\r\n[entry]\r\n10\r\n0\r\n0\r\n0\r\n\r\n" + optional + "\r\n\r\n\r\n";
        }

        private static OmsiSignalRoutes Routes(params string[] routes)
        {
            return OmsiSignalRoutes.Parse(Encoding.ASCII.GetBytes("Signal Routes File\r\n\r\n" + string.Concat(routes)));
        }

        [Test]
        public void ASignalThatIsItsOwnDistantSignalOnAnotherRouteIsSoOnTheNewOne()
        {
            var routes = Routes(Route(0, 0, 7, "[distsignal]\r\n7\r\n"), Route(1, 1, 8, "[distsignal]\r\n3\r\n"), Route(2, 1, 9, string.Empty));
            Assert.AreEqual(7, OmsiSignalRouteDefaults.DistantSignalFor(routes, 7));
            Assert.IsNull(OmsiSignalRouteDefaults.DistantSignalFor(routes, 8), "a distant signal elsewhere is no evidence for the signal itself");
            Assert.IsNull(OmsiSignalRouteDefaults.DistantSignalFor(routes, 9));
            Assert.IsNull(OmsiSignalRouteDefaults.DistantSignalFor(routes, 100), "a signal without a route has no evidence");
        }

        [Test]
        public void TheKindFollowsTheLastRouteOfTheSignalAndTheDraftEndsBeforeTheSignalOfTheWay()
        {
            var routes = Routes(Route(0, 0, 7, string.Empty), Route(1, 1, 7, string.Empty), Route(2, 0, 8, string.Empty));
            Assert.AreEqual(1, OmsiSignalRouteDefaults.KindFor(routes, 7));
            Assert.AreEqual(0, OmsiSignalRouteDefaults.KindFor(routes, 100));

            var way = new OmsiSignalRouteWay { EndSignalId = 8 };
            way.Entries.Add(new OmsiSignalRouteDraftEntry(10, 0, 0));
            way.Entries.Add(new OmsiSignalRouteDraftEntry(20, 1, 0));
            var draft = OmsiSignalRouteDefaults.DraftFor(routes, 7, way);
            Assert.AreEqual(1, draft.Kind);
            Assert.AreEqual(8, draft.NextSignal);
            Assert.IsNull(draft.DistantSignal);
            Assert.IsNull(draft.SpeedLimit);
            Assert.IsNull(draft.Aspect);
            Assert.AreEqual(2, draft.Entries.Count);
            Assert.IsNull(OmsiSignalRouteDefaults.DraftFor(routes, 7, new OmsiSignalRouteWay()).NextSignal);
        }
    }
}
