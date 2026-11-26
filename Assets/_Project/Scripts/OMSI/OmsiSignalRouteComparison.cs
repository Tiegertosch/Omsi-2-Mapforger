using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Which signal routes of the working copy differ from the original map (ADR 105, M3), so the list can mark them.</summary>
    public static class OmsiSignalRouteComparison
    {
        /// <summary>
        /// The places in <paramref name="current"/> of the routes that have no unchanged counterpart in <paramref name="original"/>. A route is
        /// unchanged when the original has a route with the same signal, the same track elements, kind, aspect, signals and speed limit; this
        /// does not depend on the place or number, so deleting a route does not mark the ones after it. The path numbers of the entries are
        /// left out: keeping them in step with edited tiles (M2) is no change of the route.
        /// </summary>
        public static HashSet<int> EditedRoutes(OmsiSignalRoutes current, OmsiSignalRoutes original)
        {
            if (current == null)
                throw new ArgumentNullException(nameof(current));
            var edited = new HashSet<int>();
            if (original == null)
                return edited;
            var currentByIdentity = new Dictionary<string, List<OmsiSignalRoute>>(StringComparer.Ordinal);
            var originalByIdentity = new Dictionary<string, List<OmsiSignalRoute>>(StringComparer.Ordinal);
            Group(current.Routes, currentByIdentity);
            Group(original.Routes, originalByIdentity);
            foreach (var group in currentByIdentity)
            {
                var routes = group.Value;
                originalByIdentity.TryGetValue(group.Key, out var ranked);
                ranked = ranked ?? new List<OmsiSignalRoute>();
                var partners = new List<OmsiSignalRoute>(ranked);
                var open = new List<OmsiSignalRoute>();
                // First the route that stands at the same place among the routes of its signal and track and is equal: an edit stays with the route that was edited.
                for (var at = 0; at < routes.Count; at++)
                {
                    var rank = at < ranked.Count ? ranked[at] : null;
                    if (rank != null && partners.Contains(rank) && Key(rank) == Key(routes[at]))
                        partners.Remove(rank);
                    else
                        open.Add(routes[at]);
                }
                // Then any equal one that is left (a route that moved within its group).
                var stillOpen = new List<OmsiSignalRoute>();
                foreach (var route in open)
                {
                    var equal = partners.FindIndex(candidate => Key(candidate) == Key(route));
                    if (equal >= 0)
                        partners.RemoveAt(equal);
                    else
                        stillOpen.Add(route);
                }
                // What remains differs from every partner left: each takes the partner it agrees with most, each partner only once, and is edited.
                foreach (var route in stillOpen)
                {
                    var best = BestPartner(route, partners);
                    if (best != null)
                        partners.Remove(best);
                    edited.Add(route.Index);
                }
            }
            return edited;
        }

        private static void Group(IReadOnlyList<OmsiSignalRoute> routes, Dictionary<string, List<OmsiSignalRoute>> groups)
        {
            foreach (var route in routes)
            {
                var identity = Identity(route);
                if (!groups.TryGetValue(identity, out var list))
                    groups[identity] = list = new List<OmsiSignalRoute>();
                list.Add(route);
            }
        }

        private static int Score(OmsiSignalRoute route, OmsiSignalRoute candidate)
        {
            return (candidate.Kind == route.Kind ? 1 : 0) + (candidate.Aspect == route.Aspect ? 1 : 0) + (candidate.DistantSignalId == route.DistantSignalId ? 1 : 0) +
                   (candidate.NextSignalId == route.NextSignalId ? 1 : 0) + (candidate.SpeedLimit == route.SpeedLimit ? 1 : 0);
        }

        private static OmsiSignalRoute BestPartner(OmsiSignalRoute route, IEnumerable<OmsiSignalRoute> candidates)
        {
            OmsiSignalRoute best = null;
            var bestScore = -1;
            foreach (var candidate in candidates)
            {
                var score = Score(route, candidate);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>
        /// The route of the original map that <paramref name="route"/> comes from: the one with the same signal and the same track elements, of those
        /// the one that agrees in most values. Null when the original has none (or no original is given).
        /// </summary>
        public static OmsiSignalRoute Counterpart(OmsiSignalRoute route, OmsiSignalRoutes original)
        {
            if (route == null || original == null)
                return null;
            var identity = Identity(route);
            return BestPartner(route, original.Routes.Where(candidate => Identity(candidate) == identity));
        }

        private static string Identity(OmsiSignalRoute route)
        {
            var culture = CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            text.Append(route.SignalId?.ToString(culture)).Append('|');
            foreach (var entry in route.Entries)
                text.Append(entry.ElementId.ToString(culture)).Append(',');
            return text.ToString();
        }

        private static string Key(OmsiSignalRoute route)
        {
            var culture = CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            text.Append(route.Kind.ToString(culture)).Append('|').Append(route.SignalId?.ToString(culture)).Append('|').Append(route.Aspect?.ToString(culture)).Append('|')
                .Append(route.DistantSignalId?.ToString(culture)).Append('|').Append(route.NextSignalId?.ToString(culture)).Append('|')
                .Append(route.SpeedLimit?.ToString("R", culture)).Append('|');
            foreach (var entry in route.Entries)
                text.Append(entry.ElementId.ToString(culture)).Append(',');
            return text.ToString();
        }
    }
}
