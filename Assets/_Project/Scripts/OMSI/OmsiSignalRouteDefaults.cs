using System;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What a new signal route starts with (ADR 105, M4), taken from the routes the map has already; no Unity.</summary>
    public static class OmsiSignalRouteDefaults
    {
        /// <summary>
        /// The distant signal of a new route of a signal: the signal itself when another route of it already names it as its distant signal, else null.
        /// On Berlin-Spandau 19 of the 49 routes (at 14 of the 39 signals) name their own signal as distant signal, and each of the 5 signals with two routes or more
        /// agrees with itself on all of them (2 are their own distant signal, 1 has another, 2 have none), while the signal file does not tell: signals of one
        /// file (Hl-HSig_4.5m, Sv_ESig_3L) are their own distant signal at one place and have none at the next. So the routes of the signal decide; a signal without
        /// a route has no evidence and gets none.
        /// </summary>
        public static int? DistantSignalFor(OmsiSignalRoutes routes, int signalId)
        {
            if (routes == null)
                throw new ArgumentNullException(nameof(routes));
            foreach (var route in routes.RoutesOfSignal(signalId))
                if (route.DistantSignalId == signalId)
                    return signalId;
            return null;
        }

        /// <summary>The kind ([signalroute]) of the last route of the signal, else 0.</summary>
        public static int KindFor(OmsiSignalRoutes routes, int signalId)
        {
            if (routes == null)
                throw new ArgumentNullException(nameof(routes));
            var own = routes.RoutesOfSignal(signalId);
            return own.Count > 0 ? own[own.Count - 1].Kind : 0;
        }

        /// <summary>A draft with the way's entries, the signal it ends before as next signal, the distant signal and kind from the signal's other routes; aspect and speed stay empty.</summary>
        public static OmsiSignalRouteDraft DraftFor(OmsiSignalRoutes routes, int signalId, OmsiSignalRouteWay way)
        {
            if (way == null)
                throw new ArgumentNullException(nameof(way));
            var draft = new OmsiSignalRouteDraft { Kind = KindFor(routes, signalId), DistantSignal = DistantSignalFor(routes, signalId), NextSignal = way.EndSignalId };
            draft.Entries.AddRange(way.Entries);
            return draft;
        }
    }
}
